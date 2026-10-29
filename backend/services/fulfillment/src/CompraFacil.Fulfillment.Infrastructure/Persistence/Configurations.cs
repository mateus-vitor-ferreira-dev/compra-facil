using CompraFacil.Fulfillment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompraFacil.Fulfillment.Infrastructure.Persistence;

/// <summary>Uma mensagem de webhook já recebida: o fornecedor repete, e a repetida não conta duas vezes.</summary>
internal sealed class SupplierWebhook
{
    public string MessageId { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
}

internal sealed class SupplierOrderConfiguration : IEntityTypeConfiguration<SupplierOrder>
{
    public void Configure(EntityTypeBuilder<SupplierOrder> builder)
    {
        builder.ToTable("supplier_orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.CustomerEmail).HasMaxLength(320);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.Supplier).HasMaxLength(32);
        builder.Property(o => o.SupplierOrderId).HasMaxLength(64);
        builder.Property(o => o.FailureReason).HasMaxLength(32);
        builder.Property(o => o.FailureDetail).HasMaxLength(500);
        builder.Property(o => o.LastError).HasMaxLength(500);
        builder.Property(o => o.Version).IsRowVersion(); // xmin: o despachante e o webhook no mesmo pedido
        // os itens como o fornecedor recebe: só leitura depois de criados, em jsonb
        builder.OwnsMany(o => o.Items, items => items.ToJson("items"));
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        // para quem e para onde: escrito uma vez, lido a cada tentativa (#221)
        builder.OwnsOne(o => o.ShipTo, t => t.ToJson("ship_to"));
        builder.OwnsOne(o => o.Tracking, t =>
        {
            t.Property(x => x.Number).HasColumnName("tracking_number").HasMaxLength(64);
            t.Property(x => x.Provider).HasColumnName("tracking_provider").HasMaxLength(64);
            t.Property(x => x.Url).HasColumnName("tracking_url").HasMaxLength(500);
        });
        // a fila do despachante: os pendentes, na ordem de chegada
        builder.HasIndex(o => new { o.Status, o.CreatedAt });
    }
}

internal sealed class SupplierWebhookConfiguration : IEntityTypeConfiguration<SupplierWebhook>
{
    public void Configure(EntityTypeBuilder<SupplierWebhook> builder)
    {
        builder.ToTable("supplier_webhooks");
        builder.HasKey(w => w.MessageId);
        builder.Property(w => w.MessageId).HasMaxLength(64);
    }
}

/// <summary>O retrato do feed do fornecedor: o que veio na última leitura de cada produto.</summary>
internal sealed class SupplierProductConfiguration : IEntityTypeConfiguration<SupplierProduct>
{
    public void Configure(EntityTypeBuilder<SupplierProduct> builder)
    {
        builder.ToTable("supplier_products");
        builder.HasKey(p => new { p.Supplier, p.Vid });
        builder.Property(p => p.Supplier).HasMaxLength(32);
        builder.Property(p => p.Vid).HasMaxLength(64);
        builder.Property(p => p.Sku).HasMaxLength(64);
        builder.Property(p => p.Name).HasMaxLength(300);
        builder.Property(p => p.Category).HasMaxLength(120);
        builder.Property(p => p.Currency).HasMaxLength(3);
        builder.Property(p => p.ImageUrl).HasMaxLength(1000);
    }
}
