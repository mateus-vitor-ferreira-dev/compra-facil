using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application.ManualSupplier;

/// <summary>O que a equipe informou ao fazer o pedido no fornecedor: o número dele e o que pagou (opcional).</summary>
public sealed record StaffPlacement(string SupplierOrderId, long? PaidProductCostCents, long? PaidFreightCostCents, string PlacedBy);

/// <summary>
/// O que a equipe faz com um pedido do fornecedor manual (#412), no lugar da API e dos webhooks dele. Cada ação muda
/// o pedido e publica o mesmo evento que o fornecedor com API geraria: o orders e as notificações não sabem a
/// diferença. "Sem estoque" segue o caminho da recusa: o orders cancela e o payments estorna.
/// </summary>
public sealed class ManualOrderActions(ISupplierOrderRepository orders, IOutbox outbox, TimeProvider clock)
{
    public const int MaxToPlace = 200;

    public Task<List<SupplierOrder>> ListToPlaceAsync(int limit, CancellationToken ct) => orders.ListAwaitingStaffAsync(limit, ct);

    public Task<SupplierOrderActionResult> PlacedAsync(Guid orderId, StaffPlacement placement, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return ApplyAsync(orderId, o => o.MarkPlacedByStaff(
            placement.SupplierOrderId.Trim(), placement.PaidProductCostCents, placement.PaidFreightCostCents, placement.PlacedBy, clock.GetUtcNow()), ct);
    }

    /// <summary>O fornecedor não tem o produto: o pedido falha como recusa por estoque, e o cliente é estornado.</summary>
    public Task<SupplierOrderActionResult> OutOfStockAsync(Guid orderId, string? detail, CancellationToken ct) =>
        ApplyAsync(orderId, o => o.Status == SupplierOrderStatus.AwaitingStaff
            ? o.Fail(SupplierFailureReasons.OutOfStock, string.IsNullOrWhiteSpace(detail) ? "o fornecedor não tinha o produto (informado pela equipe)" : detail.Trim(), clock.GetUtcNow())
            : SupplierOrderChange.None, ct);

    /// <summary>O fornecedor despachou: o cliente recebe o rastreio. Só depois de o pedido ser feito nele.</summary>
    public Task<SupplierOrderActionResult> ShippedAsync(Guid orderId, Tracking tracking, CancellationToken ct) =>
        ApplyAsync(orderId, o => o.Status == SupplierOrderStatus.Placed ? o.MarkShipped(tracking, clock.GetUtcNow()) : SupplierOrderChange.None, ct);

    public Task<SupplierOrderActionResult> DeliveredAsync(Guid orderId, CancellationToken ct) =>
        ApplyAsync(orderId, o => o.Status == SupplierOrderStatus.Shipped ? o.MarkDelivered(clock.GetUtcNow()) : SupplierOrderChange.None, ct);

    private async Task<SupplierOrderActionResult> ApplyAsync(Guid orderId, Func<SupplierOrder, SupplierOrderChange> act, CancellationToken ct) =>
        await OptimisticConcurrency.RetryAsync(orders.Reset, async () =>
        {
            if (await orders.GetAsync(orderId, ct) is not { } order || order.Supplier != ManualSupplierMode.SupplierName)
            {
                return SupplierOrderActionResult.NotFound;
            }
            var change = act(order);
            if (change == SupplierOrderChange.None)
            {
                return SupplierOrderActionResult.NotAllowed;
            }
            outbox.Publish(order, change);
            await orders.SaveChangesAsync(ct);
            return SupplierOrderActionResult.Done;
        });
}
