using CompraFacil.Inventory.Domain;

namespace CompraFacil.Inventory.Application.Stock;

/// <summary>Já existe ajuste deste produto com a mesma Idempotency-Key (corrida entre requisições).</summary>
public sealed class DuplicateAdjustmentException(string message, Exception inner) : Exception(message, inner);

/// <summary>Ajuste administrativo registrado, com o saldo resultante (resposta da Idempotency-Key).</summary>
public sealed record StockAdjustmentRecord(
    Guid ProductId, string IdempotencyKey, string RequestHash, int Delta, string Reason, string? Note,
    string Operator, int OnHandAfter, int ReservedAfter, DateTimeOffset CreatedAt);

public interface IStockRepository
{
    /// <summary>Itens de estoque rastreados (para alterar), na ordem dos ids.</summary>
    Task<List<StockItem>> GetForUpdateAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct);

    Task<StockItem?> FindAsync(Guid productId, CancellationToken ct);

    Task<List<StockItem>> ListAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct);

    void Add(StockItem item);

    Task<Reservation?> GetReservationAsync(Guid orderId, bool forUpdate, CancellationToken ct);

    void Add(Reservation reservation);

    Task<StockAdjustmentRecord?> FindAdjustmentAsync(Guid productId, string idempotencyKey, CancellationToken ct);

    void Add(StockAdjustmentRecord adjustment);

    /// <exception cref="BuildingBlocks.Data.ConcurrencyConflictException">Saldo alterado por outra transação.</exception>
    /// <exception cref="DuplicateAdjustmentException">Ajuste concorrente com a mesma chave.</exception>
    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>Descarta o que foi carregado/alterado, para tentar de novo com dados frescos.</summary>
    void Reset();
}
