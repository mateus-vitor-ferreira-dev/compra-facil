using CompraFacil.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompraFacil.Payments.Infrastructure.Persistence;

internal sealed class ChargeConfiguration : IEntityTypeConfiguration<Charge>
{
    public void Configure(EntityTypeBuilder<Charge> builder)
    {
        builder.ToTable("charges");
        builder.HasKey(c => c.OrderId);
        builder.Property(c => c.OrderId).ValueGeneratedNever();
        builder.Ignore(c => c.Amount);
        builder.Ignore(c => c.ReadyToCharge);
        builder.Property(c => c.Currency).HasMaxLength(3);
        builder.Property(c => c.CustomerEmail).HasMaxLength(254);
        builder.Property(c => c.Method).HasConversion<string>().HasMaxLength(8);
        builder.Property(c => c.CardToken).HasMaxLength(128);
        builder.Property(c => c.CardBrand).HasMaxLength(16);
        builder.Property(c => c.Version).IsRowVersion(); // xmin: dois eventos do mesmo pedido em réplicas diferentes
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(8);
        builder.Property(p => p.FailureReason).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Provider).HasMaxLength(16);
        builder.Property(p => p.ProviderReference).HasMaxLength(128);
        builder.Property(p => p.CardLastFour).HasMaxLength(4);
        builder.Ignore(p => p.IsPaid);
        builder.Ignore(p => p.IsOpen);
        builder.Property(p => p.Version).IsRowVersion(); // xmin: o webhook e o consumer no mesmo pagamento
        // o PIX: só existe no método pix, depois de gerado
        builder.ComplexProperty(p => p.Pix, pix =>
        {
            pix.Property(x => x.QrCode).HasColumnName("pix_qr_code").HasMaxLength(512);
            pix.Property(x => x.QrCodeBase64).HasColumnName("pix_qr_code_base64");
            pix.Property(x => x.TicketUrl).HasColumnName("pix_ticket_url").HasMaxLength(512);
            pix.Property(x => x.ExpiresAt).HasColumnName("pix_expires_at");
        });
        // os estornos aplicados, sempre lidos com o pagamento
        builder.OwnsMany(p => p.Refunds, r => r.ToJson("refunds"));
        builder.Navigation(p => p.Refunds).UsePropertyAccessMode(PropertyAccessMode.Field);
        // a notificação do provedor acha o pagamento pela referência dele
        builder.HasIndex(p => new { p.Provider, p.ProviderReference }).IsUnique().HasFilter("provider_reference IS NOT NULL");
        builder.Property(p => p.RefundReference).HasMaxLength(128);
        builder.ComplexProperty(p => p.Amount, m =>
        {
            m.Property(x => x.AmountCents).HasColumnName("amount_cents");
            m.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        });
        builder.HasIndex(p => p.OrderId);
    }
}
