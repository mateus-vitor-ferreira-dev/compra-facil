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

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
