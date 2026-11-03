using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Application.Invoices;
using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application.SupplierOrders;

/// <summary>data do PaymentApproved (só os campos usados): a taxa do provedor neste pagamento.</summary>
public sealed record PaymentApprovedData(Guid OrderId, long FeeCents);

/// <summary>Um pedido com a taxa do pagamento e se o cliente devolveu.</summary>
public sealed record OrderEconomicsRow(SupplierOrder Order, long? FeeCents, bool Returned);

/// <summary>Só os valores de um pedido, para a soma do período: sem os itens nem o endereço.</summary>
public sealed record OrderEconomicsValues(
    SupplierOrderStatus Status, long? RevenueCents, long? ProductCostCents, long? FreightCents, long? FeeCents, bool Returned);

/// <summary>O frete que entra na margem: o custo para a loja (#323) ou, nos pedidos de antes dele, o cobrado (eram iguais).</summary>
internal static class FreightCost
{
    public static long? Of(SupplierOrder order) => order.FreightCostCents ?? (order.ShippingService is null ? order.FreightCents : null);
}

/// <summary>Os pedidos criados em [<see cref="From"/>, <see cref="To"/>); sem um dos lados, sem limite nele.</summary>
public sealed record OrderPeriod(DateTimeOffset? From, DateTimeOffset? To);

public interface IOrderMarginRepository
{
    Task<OrderPaymentFee?> GetFeeAsync(Guid orderId, CancellationToken ct);

    /// <summary>Entra no SaveChanges do consumer.</summary>
    void AddFee(OrderPaymentFee fee);

    /// <summary>Os pedidos mais recentes do período, sem rastrear.</summary>
    Task<List<OrderEconomicsRow>> ListAsync(OrderPeriod period, int limit, CancellationToken ct);

    /// <summary>Os valores de todos os pedidos do período, para a soma (sem o limite da lista).</summary>
    Task<List<OrderEconomicsValues>> ValuesAsync(OrderPeriod period, CancellationToken ct);
}

/// <summary>PaymentApproved → guarda a taxa do provedor. O cartão aprova na autorização e de novo na captura, com a mesma taxa.</summary>
public sealed class OnPaymentApproved(IOrderMarginRepository margins) : IHandleEvent<PaymentApprovedData>
{
    public async Task HandleAsync(PaymentApprovedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (await margins.GetFeeAsync(data.OrderId, ct) is null)
        {
            margins.AddFee(OrderPaymentFee.Create(data.OrderId, data.FeeCents));
        }
    }
}

public enum OrderOutcome
{
    InProgress,
    Delivered,
    Cancelled,
    Returned,
}

/// <summary>
/// A conta de um pedido; a margem é nula se falta um valor, ou se o cliente foi estornado. FreightCents é o custo
/// do frete para a loja; FreightChargedCents, o que o cliente pagou por ele (zero no frete grátis).
/// </summary>
public sealed record OrderMargin(
    SupplierOrder Order, OrderOutcome Outcome, long? RevenueCents, long? ProductCostCents, long? FreightCents, long? FeeCents, long? MarginCents,
    long? FreightChargedCents);

/// <summary>
/// A soma de um período (#282), só dos pedidos com a margem conhecida (<see cref="Counted"/>): assim receita, custos
/// e margem fecham entre si. Os estornados (cancelados ou devolvidos) e os que não têm algum valor ficam contados
/// à parte, para a tela dizer o que ficou fora.
/// </summary>
public sealed record OrderMarginTotals(
    int Orders, int Counted, int Refunded, int Incomplete,
    long RevenueCents, long ProductCostCents, long FreightCents, long FeeCents, long MarginCents);

/// <summary>A lista do período e a soma dele.</summary>
public sealed record OrderMarginReport(IReadOnlyList<OrderMargin> Orders, OrderMarginTotals Totals);

/// <summary>
/// A margem real por pedido (#225): o que o cliente pagou, menos o custo dos produtos, o frete repassado ao
/// fornecedor e a taxa do pagamento. O pedido estornado não entra na conta: o quanto a loja perde nele depende
/// do que o fornecedor devolver. Com período (#282), a lista e a soma ficam nos pedidos criados nele.
/// </summary>
public sealed class OrderMargins(IOrderMarginRepository margins)
{
    public const int MaxLimit = 200;

    public async Task<OrderMarginReport> ReportAsync(OrderPeriod period, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(period);
        var orders = (await margins.ListAsync(period, limit, ct)).Select(From).ToList();
        var values = await margins.ValuesAsync(period, ct);
        return new OrderMarginReport(orders, Sum(values));
    }

    private static OrderMargin From(OrderEconomicsRow row)
    {
        var o = row.Order;
        var freight = FreightCost.Of(o);
        var (outcome, margin) = Compute(new OrderEconomicsValues(o.Status, o.RevenueCents, o.ProductCostCents, freight, row.FeeCents, row.Returned));
        return new OrderMargin(o, outcome, o.RevenueCents, o.ProductCostCents, freight, row.FeeCents, margin, o.FreightCents);
    }

    private static (OrderOutcome Outcome, long? MarginCents) Compute(OrderEconomicsValues v)
    {
        var outcome = v.Returned ? OrderOutcome.Returned : v.Status switch
        {
            SupplierOrderStatus.Failed => OrderOutcome.Cancelled,
            SupplierOrderStatus.Delivered => OrderOutcome.Delivered,
            _ => OrderOutcome.InProgress,
        };
        long? margin = outcome is OrderOutcome.Cancelled or OrderOutcome.Returned
            ? null
            : v.RevenueCents - v.ProductCostCents - v.FreightCents - v.FeeCents; // nulo se faltar qualquer um
        return (outcome, margin);
    }

    private static OrderMarginTotals Sum(List<OrderEconomicsValues> values)
    {
        int counted = 0, refunded = 0, incomplete = 0;
        long revenue = 0, cost = 0, freight = 0, fee = 0, margin = 0;
        foreach (var v in values)
        {
            var (outcome, m) = Compute(v);
            if (outcome is OrderOutcome.Cancelled or OrderOutcome.Returned)
            {
                refunded++;
            }
            else if (m is null)
            {
                incomplete++;
            }
            else
            {
                counted++;
                (revenue, cost, freight, fee, margin) =
                    (revenue + v.RevenueCents!.Value, cost + v.ProductCostCents!.Value, freight + v.FreightCents!.Value, fee + v.FeeCents!.Value, margin + m.Value);
            }
        }
        return new OrderMarginTotals(values.Count, counted, refunded, incomplete, revenue, cost, freight, fee, margin);
    }
}
