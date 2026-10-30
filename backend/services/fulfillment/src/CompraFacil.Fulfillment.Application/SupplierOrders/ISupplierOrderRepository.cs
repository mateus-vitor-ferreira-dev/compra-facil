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
    Task<List<SupplierOrder>> ListOverdueAsync(DateTimeOffset createdBefore, DateTimeOffset shippedBefore, DateOnly today, int limit, CancellationToken ct);

    /// <summary>Os que falharam desde <paramref name="since"/>, os mais recentes primeiro, sem rastrear.</summary>
    Task<List<SupplierOrder>> ListFailedAsync(DateTimeOffset since, int limit, CancellationToken ct);

    /// <summary>Lança ConcurrencyConflictException se outra transação mudou o pedido.</summary>
    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>Descarta o que foi carregado, para reler depois de um conflito.</summary>
    void Reset();
}
