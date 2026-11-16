using CompraFacil.Fulfillment.Application.ManualSupplier;
using CompraFacil.Fulfillment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompraFacil.Fulfillment.Infrastructure.Persistence;

internal sealed class ManualCatalogItemConfiguration : IEntityTypeConfiguration<ManualCatalogItem>
{
    public void Configure(EntityTypeBuilder<ManualCatalogItem> builder)
    {
        builder.ToTable("manual_catalog_items");
        builder.HasKey(i => i.Sku);
        builder.Property(i => i.Sku).HasMaxLength(64);
        builder.Property(i => i.Name).HasMaxLength(200);
        builder.Property(i => i.Category).HasMaxLength(100);
        builder.Property(i => i.ImageUrl).HasMaxLength(500);
    }
}

internal sealed class ManualCatalogRepository(FulfillmentDbContext db) : IManualCatalogRepository
{
    public Task<List<ManualCatalogItem>> ListAsync(CancellationToken ct) =>
        db.ManualCatalogItems.AsNoTracking().OrderBy(i => i.Sku).ToListAsync(ct);

    public async Task ReplaceAsync(IReadOnlyList<ManualCatalogItem> items, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.ManualCatalogItems.ExecuteDeleteAsync(ct);
        db.ManualCatalogItems.AddRange(items);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
    }

    public Task<DateTimeOffset?> LastImportAtAsync(CancellationToken ct) =>
        db.ManualCatalogItems.MaxAsync(i => (DateTimeOffset?)i.ImportedAt, ct);
}
