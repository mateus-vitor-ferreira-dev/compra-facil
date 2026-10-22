using CompraFacil.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompraFacil.Notifications.Infrastructure.Persistence;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever(); // = event_id
        builder.Property(n => n.Kind).HasMaxLength(32);
        builder.Property(n => n.To).HasMaxLength(254);
        builder.Property(n => n.Subject).HasMaxLength(200);
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(n => n.LastError).HasMaxLength(500);
        // fila do despachante: pendentes mais antigas primeiro
        builder.HasIndex(n => new { n.Status, n.CreatedAt });
        builder.HasIndex(n => n.OrderId);
    }
}

internal sealed class NotificationRepository(NotificationsDbContext db) : Application.Emails.INotificationRepository
{
    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) => db.Notifications.AnyAsync(n => n.Id == id, ct);

    public void Add(Notification notification) => db.Notifications.Add(notification);
}
