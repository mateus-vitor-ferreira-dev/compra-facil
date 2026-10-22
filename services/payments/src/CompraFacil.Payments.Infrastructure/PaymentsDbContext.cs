using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Payments.Domain;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Payments.Infrastructure;

/// <summary>Banco do payments (database-per-service). Tabelas e colunas em snake_case.</summary>
public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Charge> Charges => Set<Charge>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder
        .AddMessagingTables() // outbox e inbox (BuildingBlocks)
        .ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
}
