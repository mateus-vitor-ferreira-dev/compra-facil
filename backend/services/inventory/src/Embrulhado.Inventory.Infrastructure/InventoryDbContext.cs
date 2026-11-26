using Embrulhado.BuildingBlocks.Messaging;
using Embrulhado.Inventory.Application.Stock;
using Embrulhado.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Embrulhado.Inventory.Infrastructure;

/// <summary>Banco do inventory (database-per-service). Tabelas e colunas em snake_case.</summary>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<StockItem> Stock => Set<StockItem>();
    public DbSet<SupplierStockLevel> SupplierStock => Set<SupplierStockLevel>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<StockAdjustmentRecord> Adjustments => Set<StockAdjustmentRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder
        .AddMessagingTables() // outbox e inbox (BuildingBlocks)
        .ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
}
