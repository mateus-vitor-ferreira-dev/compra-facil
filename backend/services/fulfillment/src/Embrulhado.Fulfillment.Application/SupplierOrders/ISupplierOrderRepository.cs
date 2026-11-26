using Embrulhado.Fulfillment.Domain;

namespace Embrulhado.Fulfillment.Application.SupplierOrders;

public interface ISupplierOrderRepository
{
    Task<SupplierOrder?> GetAsync(Guid orderId, CancellationToken ct);

    /// <summary>Entra no SaveChanges do consumer (mesma transação do inbox e do outbox).</summary>
    void Add(SupplierOrder order);

    /// <summary>Guarda a correção do que a equipe registrou no pedido feito (#480); entra no SaveChanges.</summary>
    void AddCorrection(PlacementCorrection correction);

    /// <summary>Registra a mensagem do webhook; false se ela já tinha chegado (o fornecedor repete).</summary>
    Task<bool> TryRecordWebhookAsync(string messageId, DateTimeOffset receivedAt, CancellationToken ct);

    /// <summary>
    /// Os que não despacharam e foram pagos antes de <paramref name="createdBefore"/>, e os enviados antes de
    /// <paramref name="shippedBefore"/> que não chegaram; até <paramref name="limit"/> de cada, sem rastrear.
    /// </summary>
    Task<List<SupplierOrder>> ListOverdueAsync(DateTimeOffset createdBefore, DateTimeOffset shippedBefore, DateOnly today, int limit, CancellationToken ct);

    /// <summary>Quando chegou o último webhook do fornecedor; nulo se nunca chegou.</summary>
    Task<DateTimeOffset?> LastWebhookAtAsync(CancellationToken ct);

    /// <summary>Os que falharam desde <paramref name="since"/>, os mais recentes primeiro, sem rastrear.</summary>
    Task<List<SupplierOrder>> ListFailedAsync(DateTimeOffset since, int limit, CancellationToken ct);

    /// <summary>A fila "Pedidos a fazer" do fornecedor manual (#412): os mais antigos primeiro, sem rastrear.</summary>
    Task<List<SupplierOrder>> ListAwaitingStaffAsync(int limit, CancellationToken ct);

    /// <summary>Os do fornecedor numa etapa (o manual acompanha os feitos e os em transporte), os mais antigos primeiro.</summary>
    Task<List<SupplierOrder>> ListByStatusAsync(string supplier, SupplierOrderStatus status, int limit, CancellationToken ct);

    /// <summary>Lança ConcurrencyConflictException se outra transação mudou o pedido.</summary>
    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>Descarta o que foi carregado, para reler depois de um conflito.</summary>
    void Reset();
}
