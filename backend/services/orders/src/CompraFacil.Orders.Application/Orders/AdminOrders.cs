using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Orders.Domain;
using FluentValidation;
using FluentValidation.Results;

namespace CompraFacil.Orders.Application.Orders;

/// <summary>A lista da equipe: todos os pedidos, com filtro por situação, busca e período (criação).</summary>
public sealed record AdminOrderFilter(OrderStatus? Status, string? Search, DateTimeOffset? From, DateTimeOffset? To);

public sealed record AdminOrdersQuery(AdminOrderFilter Filter, string? Cursor, int Limit = AdminOrdersQuery.DefaultLimit)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
}

public sealed class AdminOrdersValidator : AbstractValidator<AdminOrdersQuery>
{
    public AdminOrdersValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, AdminOrdersQuery.MaxLimit);
        RuleFor(q => q.Filter.Search).MaximumLength(100).OverridePropertyName("q");
        RuleFor(q => q.Filter.To).GreaterThan(q => q.Filter.From)
            .When(q => q.Filter.From is not null && q.Filter.To is not null)
            .OverridePropertyName("to").WithMessage("deve ser depois do início do período");
    }
}

/// <summary>O período do resumo: <c>[From, To)</c>, de até um ano.</summary>
public sealed record SalesPeriod(DateTimeOffset From, DateTimeOffset To)
{
    public static readonly TimeSpan MaxLength = TimeSpan.FromDays(366);
}

public sealed class SalesPeriodValidator : AbstractValidator<SalesPeriod>
{
    public SalesPeriodValidator()
    {
        RuleFor(p => p.To).GreaterThan(p => p.From).WithMessage("deve ser depois do início do período");
        RuleFor(p => p).Must(p => p.To - p.From <= SalesPeriod.MaxLength)
            .OverridePropertyName("to").WithMessage("o período vai até 366 dias");
    }
}

public sealed record DailySales(DateOnly Date, int PaidOrders, long GrossCents, long RefundCents);

public sealed record MethodSales(PaymentMethod Method, int PaidOrders, long GrossCents);

public sealed record ProductSales(Guid ProductId, string Name, int Quantity, long RevenueCents);

/// <summary>As vendas do período, em centavos e na moeda dos pedidos.</summary>
public sealed record SalesSummary(
    SalesPeriod Period, string Currency, int Placed, int Paid, int Refunded, int NotPaid,
    long GrossCents, long RefundCents, long ShippingChargedCents,
    IReadOnlyList<DailySales> Daily, IReadOnlyList<MethodSales> ByMethod, IReadOnlyList<ProductSales> TopProducts)
{
    public long NetCents => GrossCents - RefundCents;

    public long AverageTicketCents => Paid == 0 ? 0 : (long)Math.Round((decimal)GrossCents / Paid, MidpointRounding.AwayFromZero);
}

/// <summary>
/// A leitura da equipe (operador, papel <c>admin</c>): a lista de todos os pedidos e o resumo de vendas. O
/// resumo é calculado aqui, sobre os pedidos do período: a loja é pequena, e um ano de pedidos cabe na memória.
/// </summary>
public sealed class AdminOrders(IOrderRepository orders, IValidator<AdminOrdersQuery> listValidator, IValidator<SalesPeriod> periodValidator)
{
    /// <summary>Os dias são os de Brasília: sem horário de verão desde 2019, e as imagens não têm o tzdata.</summary>
    public static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    public const int TopProductsCount = 5;

    public async Task<OrderPage> ListAsync(AdminOrdersQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        await listValidator.ValidateAndThrowAsync(query, ct);
        try
        {
            return await orders.ListAllAsync(query.Filter, query.Cursor, query.Limit, ct);
        }
        catch (InvalidCursorException)
        {
            throw new ValidationException([new ValidationFailure(nameof(query.Cursor), "cursor inválido ou de outra listagem")]);
        }
    }

    public async Task<Order> GetAsync(Guid id, CancellationToken ct) =>
        await orders.GetAsync(id, ct) ?? throw new NotFoundException($"pedido {id} não encontrado");

    /// <summary>Pago: chegou a ser cobrado. O cancelado por estoque ou pagamento recusado não conta.</summary>
    public static bool WasPaid(Order o) =>
        o.Status is OrderStatus.Confirmed or OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Returned
        || (o.Status == OrderStatus.Cancelled && o.CancelledAfterPayment);

    /// <summary>Estornado: devolvido, ou cancelado depois de pago (o estorno é do total).</summary>
    public static bool WasRefunded(Order o) =>
        o.Status == OrderStatus.Returned || (o.Status == OrderStatus.Cancelled && o.CancelledAfterPayment);

    public async Task<SalesSummary> SummaryAsync(SalesPeriod period, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(period);
        await periodValidator.ValidateAndThrowAsync(period, ct);
        var all = await orders.ListCreatedBetweenAsync(period.From, period.To, ct);
        return Summarize(period, all);
    }

    public static SalesSummary Summarize(SalesPeriod period, IReadOnlyList<Order> all)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(all);
        var currency = all.Count > 0 ? all[0].Total.Currency : "BRL";
        var paid = all.Where(WasPaid).ToList();
        var refunded = paid.Where(WasRefunded).ToList();

        // um item por dia de Brasília do período, também os dias sem venda (o gráfico mostra a falta)
        var byDay = paid.GroupBy(o => DayOf(o.CreatedAt)).ToDictionary(
            g => g.Key,
            g => (Count: g.Count(), Gross: g.Sum(o => o.Total.AmountCents), Refunds: g.Where(WasRefunded).Sum(o => o.Total.AmountCents)));
        var daily = new List<DailySales>();
        var last = DayOf(period.To - TimeSpan.FromTicks(1));
        for (var day = DayOf(period.From); day <= last; day = day.AddDays(1))
        {
            var d = byDay.GetValueOrDefault(day);
            daily.Add(new DailySales(day, d.Count, d.Gross, d.Refunds));
        }

        var byMethod = paid.GroupBy(o => o.PaymentMethod)
            .Select(g => new MethodSales(g.Key, g.Count(), g.Sum(o => o.Total.AmountCents)))
            .OrderByDescending(m => m.GrossCents).ToList();

        // o que de fato ficou vendido: pagos e não estornados
        var top = paid.Where(o => !WasRefunded(o)).SelectMany(o => o.Items)
            .GroupBy(i => i.ProductId)
            .Select(g => new ProductSales(g.Key, g.First().Name, g.Sum(i => i.Quantity), g.Sum(i => i.LineTotal.AmountCents)))
            .OrderByDescending(p => p.RevenueCents).ThenBy(p => p.Name, StringComparer.Ordinal)
            .Take(TopProductsCount).ToList();

        return new SalesSummary(
            period, currency, all.Count, paid.Count, refunded.Count, all.Count - paid.Count,
            paid.Sum(o => o.Total.AmountCents), refunded.Sum(o => o.Total.AmountCents),
            paid.Sum(o => o.Shipping?.Price.AmountCents ?? 0),
            daily, byMethod, top);
    }

    private static DateOnly DayOf(DateTimeOffset at) => DateOnly.FromDateTime(at.ToOffset(Brasilia).DateTime);
}
