using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CompraFacil.BuildingBlocks.Messaging;

/// <summary>Metadados do evento sendo tratado.</summary>
public sealed record EventContext(Guid EventId, string EventType, string Topic, string CorrelationId, DateTimeOffset OccurredAt);

/// <summary>
/// Trata um tipo de evento. Roda na mesma transação que marca o evento como processado (inbox):
/// as escritas do handler, os eventos que ele publicar (IOutbox) e o registro no inbox entram juntos.
/// </summary>
public interface IHandleEvent<in TData>
{
    Task HandleAsync(TData data, EventContext context, CancellationToken ct);
}

/// <summary>
/// Consome os tópicos dos handlers registrados, com o group = nome do serviço. Para cada mensagem:
/// envelope → handler do event_type → transação com inbox (event_id repetido é ignorado) → commit do
/// offset. event_type sem handler é pulado: é assim que o produtor evolui sem coordenar deploy.
/// Falha no handler segura a partição e tenta de novo com backoff; retries limitados e dead letter
/// topic entram na #80.
/// </summary>
public sealed partial class EventConsumer<TContext>(
    IServiceScopeFactory scopes,
    MessagingOptions options,
    ILogger<EventConsumer<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private readonly Dictionary<(string Topic, string EventType), HandlerRegistration> _handlers =
        options.Handlers.ToDictionary(h => (h.Topic, h.EventType));

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_handlers.Count == 0)
        {
            return Task.CompletedTask;
        }
        // Consume() bloqueia: roda numa thread própria para não segurar a subida do host
        return Task.Factory.StartNew(() => ConsumeLoopAsync(stoppingToken), stoppingToken,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = options.ServiceName,
            ClientId = options.ServiceName,
            EnableAutoCommit = false, // offset só depois de processar
            AutoOffsetReset = AutoOffsetReset.Earliest,
            AllowAutoCreateTopics = false, // o tópico é criado pelo dono (relay do produtor)
            TopicMetadataRefreshIntervalMs = 10_000, // tópico criado depois da subida entra logo
        }).Build();
        consumer.Subscribe(_handlers.Keys.Select(k => k.Topic).Distinct());

        while (!ct.IsCancellationRequested)
        {
            ConsumeResult<string, string> result;
            try
            {
                result = consumer.Consume(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException e)
            {
                // ex.: tópico ainda não existe; o consumer segue e pega quando for criado
                LogConsumeError(logger, e.Error.Reason);
                await DelayAsync(options.PollInterval, ct);
                continue;
            }

            var backoff = options.PollInterval;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ProcessAsync(result, ct);
                    consumer.Commit(result);
                    break;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    LogHandlerFailed(logger, e, result.Topic, result.Partition.Value, result.Offset.Value);
                    await DelayAsync(backoff, ct);
                    backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, options.MaxBackoff.Ticks));
                }
            }
        }
        consumer.Close(); // sai do group na hora, sem esperar o session timeout
    }

    /// <summary>Trata uma mensagem. Público para os testes chamarem sem o laço.</summary>
    internal async Task ProcessAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        var envelope = EventEnvelope.TryParse(result.Message.Value);
        if (envelope is null)
        {
            LogInvalidMessage(logger, result.Topic, result.Partition.Value, result.Offset.Value);
            return; // mensagem que não é envelope não volta a ser válida; segue (DLT na #80)
        }
        if (!_handlers.TryGetValue((result.Topic, envelope.EventType), out var registration))
        {
            return; // event_type que este serviço não trata
        }

        var headers = result.Message.Headers?.ToDictionary(h => h.Key, h => Encoding.UTF8.GetString(h.GetValueBytes()))
            ?? [];
        using var activity = MessagingTelemetry.Source.StartActivity(
            $"{envelope.EventType} process", ActivityKind.Consumer, MessagingTelemetry.ParentFrom(headers));
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", result.Topic);
        activity?.SetTag("messaging.message.id", envelope.EventId.ToString());

        // eventos publicados pelo handler continuam o mesmo fluxo, causados por este
        var previous = CorrelationContext.Current;
        CorrelationContext.Current = new CorrelationContext(envelope.CorrelationId, envelope.EventId);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            if (await db.Set<InboxMessage>().AnyAsync(m => m.EventId == envelope.EventId, ct))
            {
                activity?.SetTag("compra_facil.duplicate", true);
                return; // já processado (redelivery): o efeito não se repete
            }

            var context = new EventContext(envelope.EventId, envelope.EventType, result.Topic, envelope.CorrelationId, envelope.OccurredAt);
            await registration.Invoke(scope.ServiceProvider, envelope, context, ct);

            db.Set<InboxMessage>().Add(new InboxMessage
            {
                EventId = envelope.EventId,
                Topic = result.Topic,
                EventType = envelope.EventType,
                ProcessedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        finally
        {
            CorrelationContext.Current = previous;
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
            // desligando
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "erro ao consumir: {Reason}")]
    private static partial void LogConsumeError(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "handler falhou em {Topic}[{Partition}]@{Offset}; nova tentativa")]
    private static partial void LogHandlerFailed(ILogger logger, Exception e, string topic, int partition, long offset);

    [LoggerMessage(Level = LogLevel.Error, Message = "mensagem sem envelope válido em {Topic}[{Partition}]@{Offset}; ignorada")]
    private static partial void LogInvalidMessage(ILogger logger, string topic, int partition, long offset);
}
