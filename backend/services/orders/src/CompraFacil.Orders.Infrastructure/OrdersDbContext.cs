using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Orders.Domain;
using CompraFacil.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Orders.Infrastructure;

/// <summary>Banco do orders (database-per-service). Tabelas e colunas em snake_case.</summary>
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<ProductSnapshotRecord> ProductSnapshots => Set<ProductSnapshotRecord>();
    public DbSet<CampaignSnapshotRecord> CampaignSnapshots => Set<CampaignSnapshotRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder
        .AddMessagingTables() // outbox e inbox (BuildingBlocks)
        .ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
}
