using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Inventory.Application.Stock;
using CompraFacil.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompraFacil.Inventory.Infrastructure.Persistence;

internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("stock_items", t =>
        {
            // última barreira: nem um bug no código deixa o estoque negativo
            t.HasCheckConstraint("ck_stock_items_non_negative", "on_hand >= 0 AND reserved >= 0 AND reserved <= on_hand");
        });
        builder.HasKey(s => s.ProductId);
        builder.Property(s => s.ProductId).ValueGeneratedNever();
        builder.Ignore(s => s.Available);
        builder.Ignore(s => s.IsMirrored);
        builder.Property(s => s.Supplier).HasMaxLength(32);
        builder.Property(s => s.SupplierVid).HasMaxLength(64);
        builder.Property(s => s.Version).IsRowVersion(); // xmin
        // o SupplierStockChanged acha os saldos da variante
        builder.HasIndex(s => new { s.Supplier, s.SupplierVid }).HasFilter("supplier_vid IS NOT NULL");
    }
}

/// <summary>A última leitura do estoque de cada variante do fornecedor (#218).</summary>
internal sealed class SupplierStockLevelConfiguration : IEntityTypeConfiguration<SupplierStockLevel>
{
    public void Configure(EntityTypeBuilder<SupplierStockLevel> builder)
    {
        builder.ToTable("supplier_stock", t => t.HasCheckConstraint("ck_supplier_stock_non_negative", "available >= 0"));
        builder.HasKey(l => new { l.Supplier, l.Vid });
        builder.Property(l => l.Supplier).HasMaxLength(32);
        builder.Property(l => l.Vid).HasMaxLength(64);
    }
}

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations");
        builder.HasKey(r => r.OrderId);
        builder.Property(r => r.OrderId).ValueGeneratedNever();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Version).IsRowVersion();
        builder.Ignore(r => r.Items);
        builder.Property<List<ReservedItem>>("_items")
            .HasColumnName("items")
            .HasColumnType("jsonb")
            .HasConversion(
                items => JsonSerializer.Serialize(items, EventEnvelope.JsonOptions),
                json => JsonSerializer.Deserialize<List<ReservedItem>>(json, EventEnvelope.JsonOptions) ?? new List<ReservedItem>(),
                new ValueComparer<List<ReservedItem>>(
                    (a, b) => a!.SequenceEqual(b!),
                    items => items.Aggregate(0, (h, i) => HashCode.Combine(h, i.GetHashCode())),
                    items => items.ToList()));
    }
}

internal sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustmentRecord>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentRecord> builder)
    {
        builder.ToTable("stock_adjustments"); // trilha de auditoria dos ajustes manuais
        builder.HasKey(a => new { a.ProductId, a.IdempotencyKey });
        builder.Property(a => a.IdempotencyKey).HasMaxLength(128);
        builder.Property(a => a.RequestHash).HasMaxLength(64);
        builder.Property(a => a.Reason).HasMaxLength(32);
        builder.Property(a => a.Note).HasMaxLength(500);
        builder.Property(a => a.Operator).HasMaxLength(128);
    }
}
