using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

namespace CompraFacil.BuildingBlocks.Messaging;

/// <summary>
/// Move os eventos do outbox para o Kafka, em ordem. Mesmo desenho do relay do catalog
/// (services/catalog/internal/events): advisory lock para uma réplica publicar por vez, lote
/// confirmado pelo broker antes de apagar, backoff exponencial com o Kafka fora.
/// Entrega at-least-once: o consumidor deduplica por event_id.
/// </summary>
public sealed partial class OutboxRelay<TContext>(
    IServiceScopeFactory scopes,
    MessagingOptions options,
    ILogger<OutboxRelay<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private readonly long _lockKey = LockKeyFor(options.ServiceName);
    private IProducer<string, string>? _producer;
    private bool _topicsReady;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = options.PollInterval;
        var failing = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                int published;
                // laço de manutenção (consulta a cada PollInterval): sem traces, senão enterram os de
                // pedidos. A continuidade do trace da SAGA vem do traceparent gravado com o evento.
                using (SuppressInstrumentationScope.Begin())
                {
                    published = await DrainOnceAsync(stoppingToken);
                }
                if (failing)
                {
                    LogRecovered(logger);
                }
                failing = false;
                backoff = options.PollInterval;
                wait = published == options.BatchSize ? TimeSpan.Zero : options.PollInterval; // lote cheio: há fila
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                if (!failing)
                {
                    LogPublishFailed(logger, e);
                }
                failing = true;
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, options.MaxBackoff.Ticks));
                wait = backoff;
            }
            if (wait > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(wait, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Publica um lote: os eventos mais antigos, em ordem, e apaga os confirmados. Sem o lock (outra
    /// réplica publicando), devolve 0. Falha na publicação desfaz tudo: os eventos ficam no outbox.
    /// </summary>
    public async Task<int> DrainOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var locked = await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({_lockKey}) AS \"Value\"")
            .SingleAsync(ct);
        if (!locked)
        {
            return 0;
        }

        var batch = await db.Set<OutboxMessage>().AsNoTracking()
            .OrderBy(m => m.Id).Take(options.BatchSize).ToListAsync(ct);
        if (batch.Count == 0)
        {
            return 0;
        }

        await PublishAsync(batch, ct);

        var ids = batch.Select(m => m.Id).ToList();
        await db.Set<OutboxMessage>().Where(m => ids.Contains(m.Id)).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }

    private async Task PublishAsync(List<OutboxMessage> batch, CancellationToken ct)
    {
        await EnsureTopicsAsync();
        var producer = _producer ??= new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = options.ServiceName,
            // idempotente: acks=all e sem duplicar nem reordenar em retries internos
            EnableIdempotence = true,
            MessageTimeoutMs = (int)options.PublishTimeout.TotalMilliseconds,
            LingerMs = 5,
        }).Build();

        using var activity = MessagingTelemetry.Source.StartActivity("outbox publish", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.batch.message_count", batch.Count);

        // os ProduceAsync entram na fila do producer na ordem do loop; esperar todos garante o lote
        var deliveries = batch.Select(m => producer.ProduceAsync(m.Topic, new Message<string, string>
        {
            Key = m.MessageKey,
            Value = m.Payload,
            Headers = ToKafkaHeaders(m.Headers),
        }, ct)).ToList();
        await Task.WhenAll(deliveries);
        foreach (var topic in batch.GroupBy(m => m.Topic))
        {
            MessagingTelemetry.Published.Add(topic.Count(), new KeyValuePair<string, object?>("topic", topic.Key));
        }
    }

    /// <summary>Cria os tópicos do serviço (3 partições, 7 dias); existente não é alterado.</summary>
    private async Task EnsureTopicsAsync()
    {
        if (_topicsReady || options.ProducedTopics.Count == 0)
        {
            return;
        }
        await KafkaTopics.EnsureAsync(options.BootstrapServers,
            options.ProducedTopics.Select(t => (t, KafkaTopics.Retention)), options.PublishTimeout);
        _topicsReady = true;
    }

    private static Headers ToKafkaHeaders(string json)
    {
        var headers = new Headers();
        foreach (var (key, value) in JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [])
        {
            headers.Add(key, Encoding.UTF8.GetBytes(value));
        }
        return headers;
    }

    // FNV-1a de 64 bits: chave estável por serviço (o lock é por banco, mas fica explícito)
    internal static long LockKeyFor(string service)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes("outbox:" + service))
        {
            hash = (hash ^ b) * 1099511628211UL;
        }
        return unchecked((long)hash);
    }

    public override void Dispose()
    {
        _producer?.Dispose();
        base.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "falha ao publicar eventos; ficam no outbox para nova tentativa")]
    private static partial void LogPublishFailed(ILogger logger, Exception e);

    [LoggerMessage(Level = LogLevel.Information, Message = "publicação de eventos normalizada")]
    private static partial void LogRecovered(ILogger logger);
}
