using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompraFacil.Orders.Infrastructure.Persistence;

internal sealed class ProductSnapshotConfiguration : IEntityTypeConfiguration<ProductSnapshotRecord>
{
    public void Configure(EntityTypeBuilder<ProductSnapshotRecord> builder)
    {
        builder.ToTable("product_snapshots");
        builder.HasKey(p => p.ProductId);
        builder.Property(p => p.ProductId).ValueGeneratedNever();
        builder.Property(p => p.Currency).HasMaxLength(3);
        builder.Property(p => p.Status).HasMaxLength(16);
    }
}

internal sealed class CampaignSnapshotConfiguration : IEntityTypeConfiguration<CampaignSnapshotRecord>
{
    public void Configure(EntityTypeBuilder<CampaignSnapshotRecord> builder)
    {
        builder.ToTable("campaign_snapshots");
        builder.HasKey(c => c.CampaignId);
        builder.Property(c => c.CampaignId).ValueGeneratedNever();
        builder.Property(c => c.ProductIds).HasColumnType("uuid[]");
        // "campanhas deste produto vigentes agora" (fallback da #81)
        builder.HasIndex(c => c.ProductIds).HasMethod("gin");
    }
}
