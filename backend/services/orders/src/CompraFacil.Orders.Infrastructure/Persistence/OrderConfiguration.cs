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
        builder.Property(o => o.PaymentMethod).HasConversion<string>().HasMaxLength(8);
        // o PIX a pagar: só existe depois que o payments o gera
        builder.ComplexProperty(o => o.Pix, pix =>
        {
            pix.Property(x => x.QrCode).HasColumnName("pix_qr_code").HasMaxLength(512);
            pix.Property(x => x.QrCodeBase64).HasColumnName("pix_qr_code_base64");
            pix.Property(x => x.TicketUrl).HasColumnName("pix_ticket_url").HasMaxLength(512);
            pix.Property(x => x.ExpiresAt).HasColumnName("pix_expires_at");
        });
        // quem recebe e onde (#221): nulos só nos pedidos de antes do checkout pedir o endereço
        builder.ComplexProperty(o => o.Recipient, r =>
        {
            r.Property(x => x.Name).HasColumnName("recipient_name").HasMaxLength(60);
            r.Property(x => x.Cpf).HasColumnName("recipient_cpf").HasMaxLength(11);
            r.Property(x => x.Phone).HasColumnName("recipient_phone").HasMaxLength(11);
        });
        builder.ComplexProperty(o => o.ShippingAddress, a =>
        {
            a.Property(x => x.ZipCode).HasColumnName("shipping_zip_code").HasMaxLength(8);
            a.Property(x => x.Street).HasColumnName("shipping_street").HasMaxLength(60);
            a.Property(x => x.Number).HasColumnName("shipping_number").HasMaxLength(60);
            a.Property(x => x.Complement).HasColumnName("shipping_complement").HasMaxLength(60);
            a.Property(x => x.District).HasColumnName("shipping_district").HasMaxLength(60);
            a.Property(x => x.City).HasColumnName("shipping_city").HasMaxLength(60);
            a.Property(x => x.State).HasColumnName("shipping_state").HasMaxLength(2);
        });
        // o frete e o prazo prometido (#222): nulos só nos pedidos de antes do frete no checkout
        builder.ComplexProperty(o => o.Shipping, sh =>
        {
            sh.Property(x => x.Service).HasColumnName("shipping_service").HasMaxLength(32);
            sh.Property(x => x.Name).HasColumnName("shipping_name").HasMaxLength(60);
            sh.ComplexProperty(x => x.Price, m =>
            {
                m.Property(x => x.AmountCents).HasColumnName("shipping_price_cents");
                m.Property(x => x.Currency).HasColumnName("shipping_price_currency").HasMaxLength(3);
            });
            sh.Property(x => x.MinDays).HasColumnName("shipping_min_days");
            sh.Property(x => x.MaxDays).HasColumnName("shipping_max_days");
            sh.Property(x => x.PromisedBy).HasColumnName("shipping_promised_by");
        });
        // a nota fiscal (#221): só existe depois que o fulfillment a emite
        builder.ComplexProperty(o => o.Invoice, i =>
        {
            i.Property(x => x.AccessKey).HasColumnName("invoice_access_key").HasMaxLength(44);
            i.Property(x => x.Number).HasColumnName("invoice_number");
            i.Property(x => x.Series).HasColumnName("invoice_series");
            i.Property(x => x.IssuedAt).HasColumnName("invoice_issued_at");
            i.Property(x => x.CancelledAt).HasColumnName("invoice_cancelled_at");
        });
        // a nota de devolução (#223): só existe quando o cliente desiste da compra
        builder.ComplexProperty(o => o.ReturnInvoice, i =>
        {
            i.Property(x => x.AccessKey).HasColumnName("return_invoice_access_key").HasMaxLength(44);
            i.Property(x => x.Number).HasColumnName("return_invoice_number");
            i.Property(x => x.Series).HasColumnName("return_invoice_series");
            i.Property(x => x.IssuedAt).HasColumnName("return_invoice_issued_at");
        });
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
