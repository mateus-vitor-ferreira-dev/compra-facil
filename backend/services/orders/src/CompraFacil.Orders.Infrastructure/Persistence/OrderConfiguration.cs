using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Orders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompraFacil.Orders.Infrastructure.Persistence;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.CustomerEmail).HasMaxLength(254);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.CancellationReason).HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.PaymentFailureReason).HasMaxLength(32);
        // rastreio: opcional, só existe depois do envio
        builder.ComplexProperty(o => o.Tracking, t =>
        {
            t.Property(x => x.Number).HasColumnName("tracking_number").HasMaxLength(64);
            t.Property(x => x.Provider).HasColumnName("tracking_provider").HasMaxLength(64);
            t.Property(x => x.Url).HasColumnName("tracking_url").HasMaxLength(512);
        });
        builder.Property(o => o.IdempotencyKey).HasMaxLength(128);
        builder.Property(o => o.RequestHash).HasMaxLength(64);
        builder.ComplexProperty(o => o.Total, m =>
        {
            m.Property(x => x.AmountCents).HasColumnName("total_amount_cents");
            m.Property(x => x.Currency).HasColumnName("total_currency").HasMaxLength(3);
        });
        // concorrência otimista pela coluna de sistema xmin
        builder.Property(o => o.Version).IsRowVersion();

        // itens: valores imutáveis, sempre lidos com o pedido -> uma coluna jsonb
        builder.Ignore(o => o.Items);
        builder.Property<List<OrderItem>>("_items")
            .HasColumnName("items")
            .HasColumnType("jsonb")
            .HasConversion(
                items => JsonSerializer.Serialize(items, EventEnvelope.JsonOptions),
                json => JsonSerializer.Deserialize<List<OrderItem>>(json, EventEnvelope.JsonOptions) ?? new List<OrderItem>(),
                new ValueComparer<List<OrderItem>>(
                    (a, b) => a!.SequenceEqual(b!),
                    items => items.Aggregate(0, (h, i) => HashCode.Combine(h, i.GetHashCode())),
                    items => items.ToList()));

        // Idempotency-Key é por cliente; a listagem é por cliente, mais recentes primeiro
        builder.HasIndex(o => new { o.CustomerId, o.IdempotencyKey }).IsUnique();
        builder.HasIndex(o => new { o.CustomerId, o.CreatedAt, o.Id }).IsDescending(false, true, true);
    }
}
