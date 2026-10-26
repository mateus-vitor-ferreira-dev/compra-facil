using System.Buffers.Text;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Data;
using CompraFacil.Orders.Application.Orders;
using CompraFacil.Orders.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompraFacil.Orders.Infrastructure.Persistence;

internal sealed class OrderRepository(OrdersDbContext db) : IOrderRepository
{
    public Task<Order?> GetAsync(Guid id, CancellationToken ct) =>
        db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);

    public Task<Order?> GetForUpdateAsync(Guid id, CancellationToken ct) =>
        db.Orders.SingleOrDefaultAsync(o => o.Id == id, ct);

    public Task<Order?> FindByIdempotencyKeyAsync(Guid customerId, string key, CancellationToken ct) =>
        db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.CustomerId == customerId && o.IdempotencyKey == key, ct);

    public async Task<OrderPage> ListByCustomerAsync(Guid customerId, OrderStatus? status, string? cursor, int limit, CancellationToken ct)
    {
        var query = db.Orders.AsNoTracking().Where(o => o.CustomerId == customerId);
        if (status is { } s)
        {
            query = query.Where(o => o.Status == s);
        }
        if (!string.IsNullOrEmpty(cursor))
        {
            var (createdAt, id) = Decode(cursor);
            // keyset: (created_at, id) < (cursor) na mesma ordem do índice
            query = query.Where(o => o.CreatedAt < createdAt || (o.CreatedAt == createdAt && o.Id.CompareTo(id) < 0));
        }
        var page = await query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Take(limit + 1).ToListAsync(ct);

        string? next = null;
        if (page.Count > limit)
        {
            page.RemoveAt(limit);
            next = Encode(page[^1]);
        }
        return new OrderPage(page, next);
    }

    public void Add(Order order) => db.Orders.Add(order);

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new ConcurrencyConflictException("pedido alterado por outra transação", e);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && pg.ConstraintName?.Contains("idempotency_key", StringComparison.Ordinal) == true)
        {
            throw new DuplicateIdempotencyKeyException(e.Message, e);
        }
    }

    public void Reset() => db.ChangeTracker.Clear();

    private sealed record Cursor(DateTimeOffset CreatedAt, Guid Id);

    private static string Encode(Order last) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new Cursor(last.CreatedAt, last.Id)));

    private static (DateTimeOffset, Guid) Decode(string cursor)
    {
        try
        {
            var c = JsonSerializer.Deserialize<Cursor>(Base64Url.DecodeFromChars(cursor))
                ?? throw new InvalidCursorException("cursor vazio");
            return (c.CreatedAt, c.Id);
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            throw new InvalidCursorException(e.Message);
        }
    }
}
