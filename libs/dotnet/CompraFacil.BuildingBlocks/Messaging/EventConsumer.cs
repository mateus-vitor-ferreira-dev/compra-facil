using System.Diagnostics;
using System.Globalization;
using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CompraFacil.BuildingBlocks.Messaging;

/// <summary>
/// Mensagem que nunca vai ser processada (data fora do contrato): tentar de novo não adianta, então
/// ela vai direto para a DLT, sem passar pelos tópicos de retry.
/// </summary>
public sealed class PoisonMessageException(string message, Exception? inner = null) : Exception(message, inner);

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
///
/// Falha (ADR 0003): o handler tenta de novo no lugar algumas vezes (falha passageira); persistindo,
/// a mensagem vai para &lt;serviço&gt;.retry-1, -2... (cada nível com seu atraso e seu consumer, sem
/// segurar a partição de origem) e, esgotados os níveis, para &lt;serviço&gt;.dlt. Mensagem envenenada
/// vai direto para a DLT. Da DLT, volta pelo reprocessamento (<see cref="DeadLetterStore"/>).
/// </summary>
public sealed partial class EventConsumer<TContext>(
    IServiceScopeFactory scopes,
    MessagingOptions options,
    FailureProducer forwarder,
    ILogger<EventConsumer<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private readonly Dictionary<(string Topic, string EventType), HandlerRegistration> _handlers =
        options.Handlers.ToDictionary(h => (h.Topic, h.EventType));

    /// <summary>Um laço de consumo: o dos tópicos de origem (Tier 0) ou o de um nível de retry.</summary>
    private sealed record Stage(int Tier, IReadOnlyList<string> Topics, string GroupId);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_handlers.Count == 0)
        {
            return Task.CompletedTask;
        }
        var stages = new List<Stage> { new(0, [.. _handlers.Keys.Select(k => k.Topic).Distinct()], options.ServiceName) };
        for (var tier = 1; tier <= options.RetryDelays.Count; tier++)
        {
            stages.Add(new Stage(tier, [options.RetryTopic(tier)], options.RetryTopic(tier)));
        }
        // Consume() bloqueia: cada laço roda numa thread própria para não segurar a subida do host
        return Task.WhenAll(stages.Select(stage => Task.Factory.StartNew(() => ConsumeLoopAsync(stage, stoppingToken),
            stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap()));
    }

    private async Task ConsumeLoopAsync(Stage stage, CancellationToken ct)
    {
        if (stage.Tier > 0)
        {
            // os tópicos de retry são deste serviço: cria antes de assinar
            await forwarder.EnsureTopicsAsync(ct);
        }
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = stage.GroupId,
            ClientId = stage.GroupId,
            EnableAutoCommit = false, // offset só depois de processar (ou encaminhar)
            AutoOffsetReset = AutoOffsetReset.Earliest,
            AllowAutoCreateTopics = false, // o tópico é criado pelo dono (relay do produtor)
            TopicMetadataRefreshIntervalMs = 10_000, // tópico criado depois da subida entra logo
        }).Build();
        consumer.Subscribe(stage.Topics);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    result = consumer.Consume(ct);
                }
                catch (ConsumeException e)
                {
                    // ex.: tópico ainda não existe; o consumer segue e pega quando for criado
                    LogConsumeError(logger, e.Error.Reason);
                    await Task.Delay(options.PollInterval, ct);
                    continue;
                }

                if (stage.Tier > 0)
                {
                    // mesmo atraso para todo o nível: a mensagem da frente é sempre a próxima a vencer
                    // (limitada ao atraso do nível: header estranho não deixa o consumer parado além do
                    // max.poll.interval, que o tiraria do group)
                    var wait = FailureHeaders.RetryAt(result.Message.Headers) - DateTimeOffset.UtcNow;
                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait < options.RetryDelays[stage.Tier - 1] ? wait : options.RetryDelays[stage.Tier - 1], ct);
                    }
                }
                await HandleAsync(stage, result, ct);
                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // desligando: o offset da mensagem em andamento não é confirmado, ela volta na subida
        }
        consumer.Close(); // sai do group na hora, sem esperar o session timeout
    }

    /// <summary>Processa a mensagem ou a encaminha (retry seguinte ou DLT). Só sai com ela resolvida.</summary>
    private async Task HandleAsync(Stage stage, ConsumeResult<string, string> result, CancellationToken ct)
    {
        // nos tópicos de retry, o handler e o inbox são os do tópico de origem
        var topic = stage.Tier == 0 ? result.Topic : FailureHeaders.OriginalTopic(result.Message.Headers) ?? result.Topic;
        var backoff = options.PollInterval;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ProcessAsync(topic, result.Message, ct);
                return;
            }
            catch (PoisonMessageException e)
            {
                LogPoisonMessage(logger, e, result.Topic, result.Partition.Value, result.Offset.Value);
                await ForwardAsync(stage, result, topic, options.DeadLetterTopic, e, ct);
                return;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Count(topic, EventTypeOf(result.Message), "error");
                LogHandlerFailed(logger, e, result.Topic, result.Partition.Value, result.Offset.Value, attempt);
                if (attempt >= options.InPlaceAttempts)
                {
                    var next = stage.Tier < options.RetryDelays.Count ? options.RetryTopic(stage.Tier + 1) : options.DeadLetterTopic;
                    await ForwardAsync(stage, result, topic, next, e, ct);
                    return;
                }
                await Task.Delay(backoff, ct);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, options.MaxBackoff.Ticks));
            }
        }
    }

    /// <summary>
    /// Publica a mensagem (mesma chave, valor e trace) no tópico de retry ou na DLT. Com o Kafka
    /// fora, tenta até conseguir: o offset de origem só é confirmado depois.
    /// </summary>
    private async Task ForwardAsync(Stage stage, ConsumeResult<string, string> result, string topic, string target, Exception e, CancellationToken ct)
    {
        var deadLetter = target == options.DeadLetterTopic;
        var retryAt = deadLetter ? (DateTimeOffset?)null : DateTimeOffset.UtcNow + options.RetryDelays[stage.Tier];
        var headers = FailureHeaders.ForFailure(result, topic, e, retryAt);
        var backoff = options.PollInterval;
        while (true)
        {
            try
            {
                await forwarder.ProduceAsync(target, result.Message.Key, result.Message.Value, headers, ct);
                break;
            }
            catch (Exception pe) when (pe is not OperationCanceledException)
            {
                LogForwardFailed(logger, pe, target);
                await Task.Delay(backoff, ct);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, options.MaxBackoff.Ticks));
            }
        }
        Count(topic, EventTypeOf(result.Message), deadLetter ? "dead_lettered" : "retried");
        if (deadLetter)
        {
            LogDeadLettered(logger, topic, result.Partition.Value, result.Offset.Value, target);
        }
    }

    /// <summary>Trata uma mensagem do tópico (de origem). Interno para os testes chamarem sem o laço.</summary>
    internal async Task ProcessAsync(string topic, Message<string, string> message, CancellationToken ct)
    {
        var envelope = EventEnvelope.TryParse(message.Value)
            ?? throw new PoisonMessageException("mensagem sem envelope válido"); // não volta a ser válida
        if (!_handlers.TryGetValue((topic, envelope.EventType), out var registration))
        {
            Count(topic, envelope.EventType, "skipped");
            return; // event_type que este serviço não trata
        }
        var started = Stopwatch.GetTimestamp();

        var headers = message.Headers?.ToDictionary(h => h.Key, h => Encoding.UTF8.GetString(h.GetValueBytes()))
            ?? [];
        using var activity = MessagingTelemetry.Source.StartActivity(
            $"{envelope.EventType} process", ActivityKind.Consumer, MessagingTelemetry.ParentFrom(headers));
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", topic);
        activity?.SetTag("messaging.message.id", envelope.EventId.ToString());
        if (headers.TryGetValue(FailureHeaders.Attempts, out var attempts))
        {
            activity?.SetTag("compra_facil.retry.attempts", attempts);
        }

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
                Count(topic, envelope.EventType, "duplicate");
                return; // já processado (redelivery, retry ou reprocessamento): o efeito não se repete
            }

            var context = new EventContext(envelope.EventId, envelope.EventType, topic, envelope.CorrelationId, envelope.OccurredAt);
            await registration.Invoke(scope.ServiceProvider, envelope, context, ct);

            db.Set<InboxMessage>().Add(new InboxMessage
            {
                EventId = envelope.EventId,
                Topic = topic,
                EventType = envelope.EventType,
                ProcessedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            Count(topic, envelope.EventType, "processed");
            MessagingTelemetry.ProcessDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                new KeyValuePair<string, object?>("topic", topic),
                new KeyValuePair<string, object?>("event_type", envelope.EventType));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, e.Message);
            activity?.AddException(e);
            throw;
        }
        finally
        {
            CorrelationContext.Current = previous;
        }
    }

    private static string EventTypeOf(Message<string, string> message) =>
        EventEnvelope.TryParse(message.Value)?.EventType ?? "-";

    private static void Count(string topic, string eventType, string outcome) =>
        MessagingTelemetry.Consumed.Add(1,
            new KeyValuePair<string, object?>("topic", topic),
            new KeyValuePair<string, object?>("event_type", eventType),
            new KeyValuePair<string, object?>("result", outcome));

    [LoggerMessage(Level = LogLevel.Warning, Message = "erro ao consumir: {Reason}")]
    private static partial void LogConsumeError(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "handler falhou em {Topic}[{Partition}]@{Offset} (tentativa {Attempt})")]
    private static partial void LogHandlerFailed(ILogger logger, Exception e, string topic, int partition, long offset, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "mensagem fora do contrato em {Topic}[{Partition}]@{Offset}")]
    private static partial void LogPoisonMessage(ILogger logger, Exception e, string topic, int partition, long offset);

    [LoggerMessage(Level = LogLevel.Error, Message = "mensagem de {Topic}[{Partition}]@{Offset} foi para a DLT {DeadLetterTopic}")]
    private static partial void LogDeadLettered(ILogger logger, string topic, int partition, long offset, string deadLetterTopic);

    [LoggerMessage(Level = LogLevel.Warning, Message = "falha ao encaminhar para {Target}; nova tentativa")]
    private static partial void LogForwardFailed(ILogger logger, Exception e, string target);
}

/// <summary>
/// Headers que acompanham a mensagem nos tópicos de retry e na DLT. A origem (tópico, partição,
/// offset) é a da primeira falha; os headers do produtor (traceparent) seguem iguais.
/// </summary>
public static class FailureHeaders
{
    public const string OriginalTopicKey = "x-original-topic";
    public const string OriginalPartition = "x-original-partition";
    public const string OriginalOffset = "x-original-offset";
    /// <summary>Quantas vezes a mensagem passou por um nível sem ser processada.</summary>
    public const string Attempts = "x-attempts";
    /// <summary>Quando a mensagem pode ser tentada de novo (Unix ms), nos tópicos de retry.</summary>
    public const string RetryAtKey = "x-retry-at";
    public const string Error = "x-error";
    public const string FailedAt = "x-failed-at";

    private const int MaxErrorLength = 500;

    internal static Headers ForFailure(ConsumeResult<string, string> result, string topic, Exception e, DateTimeOffset? retryAt)
    {
        var source = result.Message.Headers ?? new Headers();
        var headers = new Headers();
        foreach (var h in source.Where(h => h.Key is not (Attempts or RetryAtKey or Error or FailedAt)))
        {
            headers.Add(h.Key, h.GetValueBytes());
        }
        if (Get(source, OriginalTopicKey) is null)
        {
            Set(headers, OriginalTopicKey, topic);
            Set(headers, OriginalPartition, result.Partition.Value.ToString(CultureInfo.InvariantCulture));
            Set(headers, OriginalOffset, result.Offset.Value.ToString(CultureInfo.InvariantCulture));
        }
        var attempts = int.TryParse(Get(source, Attempts), CultureInfo.InvariantCulture, out var before) ? before + 1 : 1;
        Set(headers, Attempts, attempts.ToString(CultureInfo.InvariantCulture));
        var error = $"{e.GetType().Name}: {e.Message}";
        Set(headers, Error, error.Length > MaxErrorLength ? error[..MaxErrorLength] : error);
        Set(headers, FailedAt, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        if (retryAt is { } at)
        {
            Set(headers, RetryAtKey, at.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        }
        return headers;
    }

    internal static string? Get(Headers? headers, string key) =>
        headers is not null && headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    internal static void Set(Headers headers, string key, string value)
    {
        headers.Remove(key);
        headers.Add(key, Encoding.UTF8.GetBytes(value));
    }

    internal static string? OriginalTopic(Headers? headers) => Get(headers, OriginalTopicKey);

    internal static DateTimeOffset RetryAt(Headers? headers) =>
        long.TryParse(Get(headers, RetryAtKey), CultureInfo.InvariantCulture, out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : DateTimeOffset.MinValue;
}
