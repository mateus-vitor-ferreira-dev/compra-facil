using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Orders.Application.Orders;
using CompraFacil.Orders.Domain;
using CompraFacil.Orders.Infrastructure.Live;
using CompraFacil.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Orders.Infrastructure;

/// <summary>
/// Banco do orders (database-per-service). Tabelas e colunas em snake_case. Com o
/// <see cref="OrderChangeFeed"/> (fora das ferramentas de migração), avisa os streams ao vivo no commit.
/// </summary>
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options, OrderChangeFeed? feed = null) : DbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (feed is not null)
        {
            optionsBuilder.AddInterceptors(new OrderCommitNotifier(feed));
        }
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<ProductSnapshotRecord> ProductSnapshots => Set<ProductSnapshotRecord>();
    public DbSet<CampaignSnapshotRecord> CampaignSnapshots => Set<CampaignSnapshotRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder
        .AddMessagingTables() // outbox e inbox (BuildingBlocks)
        .ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
}
