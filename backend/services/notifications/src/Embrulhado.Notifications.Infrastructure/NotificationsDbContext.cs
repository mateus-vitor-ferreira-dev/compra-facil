using Embrulhado.BuildingBlocks.Messaging;
using Embrulhado.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace Embrulhado.Notifications.Infrastructure;

/// <summary>Banco do notifications (database-per-service). Tabelas e colunas em snake_case.</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder
        .AddMessagingTables() // outbox e inbox (BuildingBlocks)
        .ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
}
