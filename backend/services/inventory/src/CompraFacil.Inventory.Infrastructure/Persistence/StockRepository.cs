using CompraFacil.BuildingBlocks.Data;
using CompraFacil.Inventory.Application.Stock;
using CompraFacil.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompraFacil.Inventory.Infrastructure.Persistence;

internal sealed class StockRepository(InventoryDbContext db) : IStockRepository
{
    public Task<List<StockItem>> GetForUpdateAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct) =>
        db.Stock.Where(s => productIds.Contains(s.ProductId)).OrderBy(s => s.ProductId).ToListAsync(ct);

    public Task<StockItem?> FindAsync(Guid productId, CancellationToken ct) =>
        db.Stock.AsNoTracking().SingleOrDefaultAsync(s => s.ProductId == productId, ct);

    public Task<List<StockItem>> ListAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct) =>
        db.Stock.AsNoTracking().Where(s => productIds.Contains(s.ProductId)).ToListAsync(ct);

    public void Add(StockItem item) => db.Stock.Add(item);

    public Task<List<StockItem>> GetLinkedForUpdateAsync(string supplier, string vid, CancellationToken ct) =>
        db.Stock.Where(s => s.Supplier == supplier && s.SupplierVid == vid).OrderBy(s => s.ProductId).ToListAsync(ct);

    public Task<List<SupplierStockLevel>> GetSupplierLevelsAsync(string supplier, IReadOnlyCollection<string> vids, CancellationToken ct) =>
        db.SupplierStock.Where(l => l.Supplier == supplier && vids.Contains(l.Vid)).ToListAsync(ct);

    public void Add(SupplierStockLevel level) => db.SupplierStock.Add(level);

    public Task<Reservation?> GetReservationAsync(Guid orderId, bool forUpdate, CancellationToken ct) =>
        (forUpdate ? db.Reservations : db.Reservations.AsNoTracking()).SingleOrDefaultAsync(r => r.OrderId == orderId, ct);

    public void Add(Reservation reservation) => db.Reservations.Add(reservation);

    public Task<StockAdjustmentRecord?> FindAdjustmentAsync(Guid productId, string idempotencyKey, CancellationToken ct) =>
        db.Adjustments.AsNoTracking().SingleOrDefaultAsync(a => a.ProductId == productId && a.IdempotencyKey == idempotencyKey, ct);

    public void Add(StockAdjustmentRecord adjustment) => db.Adjustments.Add(adjustment);

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new ConcurrencyConflictException("saldo alterado por outra transação", e);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            // ajuste com a mesma chave gravado por outra requisição; ou saldo criado ao mesmo tempo
            // (ProductCreated × ajuste): a nova tentativa já encontra a linha
            throw pg.TableName == "stock_adjustments"
                ? new DuplicateAdjustmentException("ajuste concorrente com a mesma Idempotency-Key", e)
                : new ConcurrencyConflictException("saldo criado por outra transação", e);
        }
    }

    public void Reset() => db.ChangeTracker.Clear();
}
