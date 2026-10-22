using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Orders.Infrastructure;

/// <summary>Banco do orders (database-per-service). Tabelas e colunas em snake_case.</summary>
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
}
