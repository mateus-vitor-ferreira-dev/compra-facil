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

    /// <summary>Do despacho à entrega: o transporte.</summary>
    public TimeSpan DeliverWithin { get; set; } = TimeSpan.FromDays(15);
}

/// <summary>Em que etapa o pedido parou: o fornecedor não despachou, ou o envio não chegou.</summary>
public enum OverdueStage
{
    Shipping,
    Delivery,
}

public sealed record OverdueOrder(SupplierOrder Order, OverdueStage Stage, DateTimeOffset DueAt);

/// <summary>Os pedidos parados além do prazo do fornecedor, os mais atrasados primeiro.</summary>
public sealed class OverdueOrders(ISupplierOrderRepository orders, IOptions<SupplierDeadlines> deadlines, TimeProvider clock)
{
    public const int MaxLimit = 200;

    public async Task<IReadOnlyList<OverdueOrder>> ListAsync(int limit, CancellationToken ct)
    {
        var d = deadlines.Value;
        var now = clock.GetUtcNow();
        var found = await orders.ListOverdueAsync(now - d.ShipWithin, now - d.DeliverWithin, limit, ct);
        return [.. found
            .Select(o => o.Status == SupplierOrderStatus.Shipped
                ? new OverdueOrder(o, OverdueStage.Delivery, o.ShippedAt!.Value + d.DeliverWithin)
                : new OverdueOrder(o, OverdueStage.Shipping, o.CreatedAt + d.ShipWithin))
            .OrderBy(o => o.DueAt)
            .Take(limit)];
    }
}
