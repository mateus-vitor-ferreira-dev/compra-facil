using Microsoft.EntityFrameworkCore;

namespace CompraFacil.BuildingBlocks.Messaging;

/// <summary>
/// Evento gravado na transação da escrita, esperando o relay publicar. Mesma tabela do catalog
/// (services/catalog/migrations/000002_outbox.up.sql).
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Ordem de publicação (identity).</summary>
    public long Id { get; private set; }
    public Guid EventId { get; init; }
    public required string Topic { get; init; }
    public required string MessageKey { get; init; }
    public required string EventType { get; init; }
    /// <summary>Envelope serializado; coluna json (não jsonb) para publicar o texto exato.</summary>
    public required string Payload { get; init; }
    /// <summary>Headers Kafka em JSON (traceparent/tracestate).</summary>
    public required string Headers { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Evento já tratado por este serviço: deduplicação por event_id (entrega at-least-once).</summary>
public sealed class InboxMessage
{
    public Guid EventId { get; init; }
    public required string Topic { get; init; }
    public required string EventType { get; init; }
    public DateTimeOffset ProcessedAt { get; init; }
}

public static class MessagingModelBuilderExtensions
{
    /// <summary>Mapeia as tabelas outbox e inbox no DbContext do serviço (chamar no OnModelCreating).</summary>
    public static ModelBuilder AddMessagingTables(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).UseIdentityAlwaysColumn();
            e.HasIndex(m => m.EventId).IsUnique();
            e.Property(m => m.Payload).HasColumnType("json");
            e.Property(m => m.Headers).HasColumnType("json");
            e.Property(m => m.CreatedAt).HasDefaultValueSql("now()");
        });
        modelBuilder.Entity<InboxMessage>(e =>
        {
            e.ToTable("inbox");
            e.HasKey(m => m.EventId);
            e.Property(m => m.ProcessedAt).HasDefaultValueSql("now()");
        });
        return modelBuilder;
    }
}
