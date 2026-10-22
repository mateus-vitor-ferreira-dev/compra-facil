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
        builder.Property(p => p.FailureReason).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Provider).HasMaxLength(16);
        builder.Property(p => p.ProviderReference).HasMaxLength(128);
        builder.ComplexProperty(p => p.Amount, m =>
        {
            m.Property(x => x.AmountCents).HasColumnName("amount_cents");
            m.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        });
        builder.HasIndex(p => p.OrderId);
    }
}
