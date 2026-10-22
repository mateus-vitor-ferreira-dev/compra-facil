using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Inventory.Infrastructure;

/// <summary>Banco do inventory (database-per-service). Tabelas e colunas em snake_case.</summary>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
}
