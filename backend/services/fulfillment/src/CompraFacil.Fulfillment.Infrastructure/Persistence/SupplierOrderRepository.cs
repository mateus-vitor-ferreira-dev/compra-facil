using CompraFacil.BuildingBlocks.Data;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Fulfillment.Infrastructure.Persistence;

internal sealed class SupplierOrderRepository(FulfillmentDbContext db) : ISupplierOrderRepository
{
    public Task<SupplierOrder?> GetAsync(Guid orderId, CancellationToken ct) =>
        db.SupplierOrders.SingleOrDefaultAsync(o => o.Id == orderId, ct);

    public void Add(SupplierOrder order) => db.SupplierOrders.Add(order);

    public async Task<bool> TryRecordWebhookAsync(string messageId, DateTimeOffset receivedAt, CancellationToken ct)
    {
        if (await db.SupplierWebhooks.AnyAsync(w => w.MessageId == messageId, ct))
        {
            return false;
        }
        // dois iguais ao mesmo tempo: a chave primária barra o segundo no SaveChanges (ele recebe 500 e
        // o fornecedor repete, já como duplicado)
        db.SupplierWebhooks.Add(new SupplierWebhook { MessageId = messageId, ReceivedAt = receivedAt });
        return true;
    }

    public async Task<List<SupplierOrder>> ListOverdueAsync(
        DateTimeOffset createdBefore, DateTimeOffset shippedBefore, DateOnly today, int limit, CancellationToken ct)
    {
        var notShipped = await db.SupplierOrders.AsNoTracking()
            .Where(o => (o.Status == SupplierOrderStatus.Pending || o.Status == SupplierOrderStatus.Placing || o.Status == SupplierOrderStatus.Placed)
                && o.CreatedAt < createdBefore)
            .OrderBy(o => o.CreatedAt).Take(limit).ToListAsync(ct);
        var notDelivered = await db.SupplierOrders.AsNoTracking()
            // a data prometida vale quando existe; o pedido de antes do #222 usa o prazo de transporte
            .Where(o => o.Status == SupplierOrderStatus.Shipped
                && (o.PromisedBy != null ? o.PromisedBy < today : o.ShippedAt < shippedBefore))
            .OrderBy(o => o.ShippedAt).Take(limit).ToListAsync(ct);
        return [.. notShipped, .. notDelivered];
    }

    public Task<DateTimeOffset?> LastWebhookAtAsync(CancellationToken ct) =>
        db.SupplierWebhooks.MaxAsync(w => (DateTimeOffset?)w.ReceivedAt, ct);

    public Task<List<SupplierOrder>> ListFailedAsync(DateTimeOffset since, int limit, CancellationToken ct) =>
        db.SupplierOrders.AsNoTracking()
            .Where(o => o.Status == SupplierOrderStatus.Failed && o.FailedAt >= since)
            .OrderByDescending(o => o.FailedAt).Take(limit).ToListAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new ConcurrencyConflictException("o pedido mudou no meio da gravação", e);
        }
    }

    public void Reset() => db.ChangeTracker.Clear();
}
