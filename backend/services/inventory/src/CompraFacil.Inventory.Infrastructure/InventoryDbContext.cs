using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Inventory.Application.Stock;
using CompraFacil.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Inventory.Infrastructure;

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
