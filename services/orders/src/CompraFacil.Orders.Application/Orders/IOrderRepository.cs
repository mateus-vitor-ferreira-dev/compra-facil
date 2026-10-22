using CompraFacil.Orders.Domain;

namespace CompraFacil.Orders.Application.Orders;

public sealed record OrderPage(IReadOnlyList<Order> Items, string? NextCursor);

/// <summary>Já existe pedido deste cliente com a mesma Idempotency-Key (corrida entre requisições).</summary>
public sealed class DuplicateIdempotencyKeyException : Exception
{
    public DuplicateIdempotencyKeyException() : base("Idempotency-Key já usada por este cliente") { }

    public DuplicateIdempotencyKeyException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Cursor de paginação corrompido ou de outra listagem.</summary>
public sealed class InvalidCursorException(string message) : Exception(message);

public interface IOrderRepository
{
    Task<Order?> GetAsync(Guid id, CancellationToken ct);

    Task<Order?> FindByIdempotencyKeyAsync(Guid customerId, string key, CancellationToken ct);

    /// <summary>Do mais recente para o mais antigo, paginado por cursor opaco.</summary>
    Task<OrderPage> ListByCustomerAsync(Guid customerId, OrderStatus? status, string? cursor, int limit, CancellationToken ct);

    void Add(Order order);

    /// <summary>Grava o pedido e os eventos do outbox na mesma transação.</summary>
    /// <exception cref="DuplicateIdempotencyKeyException">Outra requisição criou o pedido com a mesma chave.</exception>
    Task SaveChangesAsync(CancellationToken ct);
}
