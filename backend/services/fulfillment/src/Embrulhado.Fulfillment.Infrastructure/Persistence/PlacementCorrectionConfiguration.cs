using Embrulhado.Fulfillment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Embrulhado.Fulfillment.Infrastructure.Persistence;

/// <summary>O histórico das correções do pedido feito no fornecedor manual (#480): só cresce.</summary>
internal sealed class PlacementCorrectionConfiguration : IEntityTypeConfiguration<PlacementCorrection>
{
    public void Configure(EntityTypeBuilder<PlacementCorrection> builder)
    {
        builder.ToTable("placement_corrections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.CorrectedBy).HasMaxLength(320);
        builder.Property(c => c.SupplierOrderId).HasMaxLength(100);
        builder.Property(c => c.PreviousSupplierOrderId).HasMaxLength(100);
        builder.HasIndex(c => c.OrderId);
    }
}
