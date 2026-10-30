using CompraFacil.Fulfillment.Domain;
using Microsoft.Extensions.Options;

namespace CompraFacil.Fulfillment.Application.SupplierOrders;

/// <summary>
/// Prazos do fornecedor (seção Supplier:Deadlines). Passado o prazo, o pedido aparece no painel para a
/// equipe cobrar o fornecedor ou o cliente: a loja responde pelo prazo que prometeu (CDC).
/// </summary>
public sealed class SupplierDeadlines
{
    public const string Section = "Supplier:Deadlines";

    /// <summary>Do pedido pago ao despacho: o tempo de separação do fornecedor.</summary>
    public TimeSpan ShipWithin { get; set; } = TimeSpan.FromDays(3);

    /// <summary>Do despacho à entrega: o transporte (só no pedido sem a data prometida, de antes do #222).</summary>
    public TimeSpan DeliverWithin { get; set; } = TimeSpan.FromDays(15);

    /// <summary>Do pedido de devolução à resposta do fornecedor.</summary>
    public TimeSpan ReturnWithin { get; set; } = TimeSpan.FromDays(15);
}

/// <summary>Em que etapa o pedido parou: o fornecedor não despachou, ou o envio não chegou.</summary>
public enum OverdueStage
{
    Shipping,
    Delivery,
}

public sealed record OverdueOrder(SupplierOrder Order, OverdueStage Stage, DateTimeOffset DueAt);

/// <summary>
/// Os pedidos parados além do prazo, os mais atrasados primeiro. A entrega vence na data prometida ao cliente
/// (#222), ao fim do dia em Brasília; sem ela, no prazo de transporte contado do envio.
/// </summary>
public sealed class OverdueOrders(ISupplierOrderRepository orders, IOptions<SupplierDeadlines> deadlines, TimeProvider clock)
{
    public const int MaxLimit = 200;

    // Brasília: UTC-3 o ano todo desde o fim do horário de verão (2019). Fixo, porque a imagem do serviço
    // (chiseled) não traz a base de fusos (tzdata)
    private static readonly TimeSpan s_brasilia = TimeSpan.FromHours(-3);

    /// <summary>O último instante do dia prometido, em Brasília.</summary>
    private static DateTimeOffset EndOfDay(DateOnly day)
    {
        return new DateTimeOffset(day.ToDateTime(new TimeOnly(23, 59, 59)), s_brasilia);
    }

    public async Task<IReadOnlyList<OverdueOrder>> ListAsync(int limit, CancellationToken ct)
    {
        var d = deadlines.Value;
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.ToOffset(s_brasilia).DateTime);
        var found = await orders.ListOverdueAsync(now - d.ShipWithin, now - d.DeliverWithin, today, limit, ct);
        return [.. found
            .Select(o => o.Status == SupplierOrderStatus.Shipped
                ? new OverdueOrder(o, OverdueStage.Delivery, o.PromisedBy is { } promised ? EndOfDay(promised) : o.ShippedAt!.Value + d.DeliverWithin)
                : new OverdueOrder(o, OverdueStage.Shipping, o.CreatedAt + d.ShipWithin))
            .OrderBy(o => o.DueAt)
            .Take(limit)];
    }
}
