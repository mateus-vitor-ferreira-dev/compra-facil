using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompraFacil.Fulfillment.Infrastructure.Persistence;

internal sealed class SupplierReturnConfiguration : IEntityTypeConfiguration<SupplierReturn>
{
    public void Configure(EntityTypeBuilder<SupplierReturn> builder)
    {
        builder.ToTable("supplier_returns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Supplier).HasMaxLength(32);
        builder.Property(r => r.SupplierOrderId).HasMaxLength(64);
        builder.Property(r => r.CustomerEmail).HasMaxLength(320);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.DisputeId).HasMaxLength(64);
        builder.Property(r => r.ReturnCode).HasMaxLength(64);
        builder.Property(r => r.ReturnAddress).HasMaxLength(500);
        builder.Property(r => r.ResolutionReason).HasMaxLength(500);
        builder.Property(r => r.LastError).HasMaxLength(500);
        builder.Property(r => r.Version).IsRowVersion(); // xmin: o laço da abertura e o webhook
        // os itens como foram ao fornecedor: só leitura depois de criados, em jsonb
        builder.Ignore(r => r.Items);
        builder.Property<List<SupplierOrderItem>>("_items")
            .HasColumnName("items")
            .HasColumnType("jsonb")
            .HasConversion(
                items => JsonSerializer.Serialize(items, EventEnvelope.JsonOptions),
                json => JsonSerializer.Deserialize<List<SupplierOrderItem>>(json, EventEnvelope.JsonOptions) ?? new List<SupplierOrderItem>(),
                new ValueComparer<List<SupplierOrderItem>>(
                    (a, b) => a!.SequenceEqual(b!),
                    items => items.Aggregate(0, (h, i) => HashCode.Combine(h, i.GetHashCode())),
                    items => items.ToList()));
        // a fila da abertura e a lista do painel
        builder.HasIndex(r => new { r.Status, r.RequestedAt });
    }
}

internal sealed class SupplierReturnRepository(FulfillmentDbContext db) : ISupplierReturnRepository
{
    public Task<SupplierReturn?> GetAsync(Guid orderId, CancellationToken ct) =>
        db.SupplierReturns.SingleOrDefaultAsync(r => r.Id == orderId, ct);

    public void Add(SupplierReturn supplierReturn) => db.SupplierReturns.Add(supplierReturn);

    public Task<List<SupplierReturn>> ListPendingAsync(DateTimeOffset rejectedSince, int limit, CancellationToken ct) =>
        db.SupplierReturns.AsNoTracking()
            .Where(r => r.Status == SupplierReturnStatus.Pending || r.Status == SupplierReturnStatus.Open
                || (r.Status == SupplierReturnStatus.Rejected && r.ResolvedAt >= rejectedSince))
            .OrderBy(r => r.RequestedAt).Take(limit).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
