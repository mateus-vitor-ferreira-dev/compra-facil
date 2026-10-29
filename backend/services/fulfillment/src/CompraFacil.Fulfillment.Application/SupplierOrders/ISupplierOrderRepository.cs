using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application.SupplierOrders;

public interface ISupplierOrderRepository
{
    Task<SupplierOrder?> GetAsync(Guid orderId, CancellationToken ct);

    /// <summary>Entra no SaveChanges do consumer (mesma transação do inbox e do outbox).</summary>
    void Add(SupplierOrder order);

    /// <summary>Registra a mensagem do webhook; false se ela já tinha chegado (o fornecedor repete).</summary>
    Task<bool> TryRecordWebhookAsync(string messageId, DateTimeOffset receivedAt, CancellationToken ct);

    /// <summary>
    /// Os que não despacharam e foram pagos antes de <paramref name="createdBefore"/>, e os enviados antes de
    /// <paramref name="shippedBefore"/> que não chegaram; até <paramref name="limit"/> de cada, sem rastrear.
    /// </summary>
    Task<List<SupplierOrder>> ListOverdueAsync(DateTimeOffset createdBefore, DateTimeOffset shippedBefore, int limit, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
