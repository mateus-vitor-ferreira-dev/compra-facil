using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace CompraFacil.BuildingBlocks.Messaging;

/// <summary>
/// Publicação de eventos pelos casos de uso. O evento entra no change tracker do DbContext do
/// serviço e é gravado no mesmo SaveChanges da escrita: ou os dois entram, ou nenhum.
/// </summary>
public interface IOutbox
{
    /// <summary>Agenda o evento para publicação.</summary>
    /// <param name="topic">Tópico do contrato AsyncAPI (ex.: orders.order.v1).</param>
    /// <param name="key">Chave da mensagem: id do agregado, garante ordem por pedido/produto.</param>
    /// <param name="eventType">event_type do contrato (ex.: OrderPlaced).</param>
    /// <param name="data">Conteúdo de data; serializado em snake_case.</param>
    /// <param name="eventVersion">Versão do formato de data para este event_type.</param>
    /// <returns>O event_id gerado.</returns>
    Guid Add<TData>(string topic, string key, string eventType, TData data, int eventVersion = 1);
}

internal sealed class Outbox<TContext>(TContext db, MessagingOptions options, TimeProvider clock) : IOutbox
    where TContext : DbContext
{
    public Guid Add<TData>(string topic, string key, string eventType, TData data, int eventVersion = 1)
    {
        var correlation = CorrelationContext.Current;
        var envelope = new EventEnvelope(
            EventId: Guid.CreateVersion7(),
            EventType: eventType,
            EventVersion: eventVersion,
            OccurredAt: clock.GetUtcNow(),
            Producer: options.ServiceName,
            // sem contexto (job, seed), o evento começa um fluxo próprio
            CorrelationId: correlation?.CorrelationId ?? Guid.NewGuid().ToString(),
            CausationId: correlation?.CausationId,
            Data: JsonSerializer.SerializeToElement(data, EventEnvelope.JsonOptions));

        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            EventId = envelope.EventId,
            Topic = topic,
            MessageKey = key,
            EventType = eventType,
            Payload = envelope.Serialize(),
            Headers = JsonSerializer.Serialize(MessagingTelemetry.CurrentTraceHeaders()),
            CreatedAt = envelope.OccurredAt,
        });
        return envelope.EventId;
    }
}

/// <summary>Spans de publicação/consumo e propagação do trace W3C pelos headers do Kafka.</summary>
public static class MessagingTelemetry
{
    public const string SourceName = "CompraFacil.Messaging";
    internal static readonly ActivitySource Source = new(SourceName);

    private static readonly Meter s_meter = new(SourceName);

    /// <summary>Eventos lidos do Kafka por resultado (processed, duplicate, skipped, poison, error).</summary>
    internal static readonly Counter<long> Consumed = s_meter.CreateCounter<long>(
        "compra_facil.messaging.consumed", unit: "{event}", description: "Eventos consumidos por tópico, tipo e resultado.");

    internal static readonly Histogram<double> ProcessDuration = s_meter.CreateHistogram<double>(
        "compra_facil.messaging.process.duration", unit: "s", description: "Tempo para tratar um evento (handler + inbox).");

    /// <summary>Eventos que o relay publicou (confirmados pelo broker).</summary>
    internal static readonly Counter<long> Published = s_meter.CreateCounter<long>(
        "compra_facil.messaging.published", unit: "{event}", description: "Eventos publicados pelo outbox, por tópico.");

    private static TextMapPropagator Propagator => Propagators.DefaultTextMapPropagator;

    internal static Dictionary<string, string> CurrentTraceHeaders()
    {
        var headers = new Dictionary<string, string>();
        if (Activity.Current is { } activity)
        {
            Propagator.Inject(new PropagationContext(activity.Context, Baggage.Current), headers,
                static (h, k, v) => h[k] = v);
        }
        return headers;
    }

    internal static ActivityContext ParentFrom(IReadOnlyDictionary<string, string> headers) =>
        Propagator.Extract(default, headers,
            static (h, k) => h.TryGetValue(k, out var v) ? [v] : []).ActivityContext;
}
