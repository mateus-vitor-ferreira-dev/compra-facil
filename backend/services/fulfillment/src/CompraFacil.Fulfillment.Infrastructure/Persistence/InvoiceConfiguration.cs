using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompraFacil.Fulfillment.Infrastructure.Persistence;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.CustomerEmail).HasMaxLength(320);
        builder.Property(i => i.PaymentMethod).HasMaxLength(8);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.ProviderId).HasMaxLength(64);
        builder.Property(i => i.AccessKey).HasMaxLength(44);
        builder.Property(i => i.RejectionReason).HasMaxLength(500);
        builder.Property(i => i.CancelError).HasMaxLength(500);
        builder.Property(i => i.Version).IsRowVersion(); // xmin: o despachante e o laço do cancelamento
        builder.OwnsOne(i => i.ShipTo, t => t.ToJson("ship_to"));
        // os itens como saem na nota: só leitura depois de criados, em jsonb
        builder.Ignore(i => i.Lines);
        builder.Property<List<InvoiceLine>>("_lines")
            .HasColumnName("lines")
            .HasColumnType("jsonb")
            .HasConversion(
                lines => JsonSerializer.Serialize(lines, EventEnvelope.JsonOptions),
                json => JsonSerializer.Deserialize<List<InvoiceLine>>(json, EventEnvelope.JsonOptions) ?? new List<InvoiceLine>(),
                new ValueComparer<List<InvoiceLine>>(
                    (a, b) => a!.SequenceEqual(b!),
                    lines => lines.Aggregate(0, (h, l) => HashCode.Combine(h, l.GetHashCode())),
                    lines => lines.ToList()));
        // a fila do cancelamento: as autorizadas com o estorno pedido
        builder.HasIndex(i => new { i.Status, i.CancelRequestedAt });
    }
}
