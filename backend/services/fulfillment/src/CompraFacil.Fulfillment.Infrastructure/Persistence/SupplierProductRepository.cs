using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Domain;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Fulfillment.Infrastructure.Persistence;

internal sealed class SupplierProductRepository(FulfillmentDbContext db) : ISupplierProductRepository
{
    // chave do lock consultivo da sincronização do catálogo (qualquer número fixo, único no banco)
    private const long SyncLockKey = 2_170_001;

    public async Task<bool> RunExclusiveAsync(Func<Task> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // o lock some com a transação, inclusive se o processo cair no meio
        var locked = await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({SyncLockKey}) AS \"Value\"")
            .SingleAsync(ct);
        if (!locked)
        {
            return false;
        }
        await work();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public Task<List<SupplierProduct>> ListAsync(string supplier, CancellationToken ct) =>
        db.SupplierProducts.Where(p => p.Supplier == supplier).ToListAsync(ct);

    public void Add(SupplierProduct product) => db.SupplierProducts.Add(product);
}
