using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Notifications.Infrastructure;

/// <summary>Banco do notifications (database-per-service). Tabelas e colunas em snake_case.</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
}
