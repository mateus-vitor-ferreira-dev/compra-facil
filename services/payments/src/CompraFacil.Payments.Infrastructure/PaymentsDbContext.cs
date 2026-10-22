using CompraFacil.BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Payments.Infrastructure;

/// <summary>Banco do payments (database-per-service). Tabelas e colunas em snake_case.</summary>
public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder
        .AddMessagingTables() // outbox e inbox (BuildingBlocks)
        .ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
}
