using System.Globalization;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace CompraFacil.BuildingBlocks.Messaging;

/// <summary>
/// Producer dos tópicos de retry e da DLT do serviço (o consumer encaminha, o reprocessamento
/// devolve). Separado do relay: não passa pelo outbox, a mensagem já existe no Kafka.
/// </summary>
public sealed class FailureProducer(MessagingOptions options) : IDisposable
{
    private readonly Lazy<IProducer<string, string>> _producer = new(() => new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = options.BootstrapServers,
        ClientId = options.ServiceName + "-failures",
        EnableIdempotence = true,
        MessageTimeoutMs = (int)options.PublishTimeout.TotalMilliseconds,
    }).Build());

    private readonly SemaphoreSlim _topicsLock = new(1, 1);
    private bool _topicsReady;

    /// <summary>Cria os tópicos de retry e a DLT; com o Kafka fora, tenta até conseguir.</summary>
    internal async Task EnsureTopicsAsync(CancellationToken ct)
    {
        var backoff = options.PollInterval;
        while (!_topicsReady)
        {
            await _topicsLock.WaitAsync(ct);
            try
            {
                if (!_topicsReady)
                {
                    var topics = Enumerable.Range(1, options.RetryDelays.Count)
                        .Select(tier => (options.RetryTopic(tier), KafkaTopics.Retention))
                        .Append((options.DeadLetterTopic, KafkaTopics.DeadLetterRetention));
                    await KafkaTopics.EnsureAsync(options.BootstrapServers, topics, options.PublishTimeout);
                    _topicsReady = true;
                }
            }
            catch (KafkaException)
            {
                await Task.Delay(backoff, ct);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, options.MaxBackoff.Ticks));
            }
            finally
            {
                _topicsLock.Release();
            }
        }
    }

    internal async Task ProduceAsync(string topic, string key, string value, Headers headers, CancellationToken ct)
    {
        await EnsureTopicsAsync(ct);
        await _producer.Value.ProduceAsync(topic, new Message<string, string> { Key = key, Value = value, Headers = headers }, ct);
    }

    public void Dispose()
    {
        if (_producer.IsValueCreated)
        {
            _producer.Value.Flush(options.PublishTimeout);
            _producer.Value.Dispose();
        }
        _topicsLock.Dispose();
    }
}

/// <summary>Mensagem parada na DLT, com a origem e o último erro.</summary>
public sealed record DeadLetter(
    Guid? EventId,
    string? EventType,
    string Topic,
    int? Partition,
    long? Offset,
    int Attempts,
    string? Error,
    DateTimeOffset? FailedAt);

/// <summary>
/// A DLT do serviço: lista o que está pendente e reprocessa. A posição do reprocessamento é o
/// consumer group &lt;serviço&gt;.dlt-replay: pendente é o que veio depois do último reprocessamento.
/// Reprocessar devolve as mensagens para &lt;serviço&gt;.retry-1 (só este serviço as consome de
/// novo); falhando outra vez, elas voltam para a DLT pelo caminho normal. O inbox garante que
/// reprocessar algo já tratado não repete o efeito.
/// </summary>
public sealed partial class DeadLetterStore(MessagingOptions options, FailureProducer producer, ILogger<DeadLetterStore> logger)
    : IDisposable
{
    private static readonly TimeSpan s_kafkaTimeout = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _replayLock = new(1, 1);

    private string ReplayGroup => options.DeadLetterTopic + "-replay";

    /// <summary>Até <paramref name="limit"/> mensagens pendentes, da mais antiga para a mais nova (por partição).</summary>
    public async Task<IReadOnlyList<DeadLetter>> PendingAsync(int limit, CancellationToken ct)
    {
        var found = new List<DeadLetter>();
        await ReadPendingAsync(limit, (result, _) =>
        {
            found.Add(Describe(result));
            return Task.CompletedTask;
        }, commit: false, ct);
        return found;
    }

    /// <summary>Devolve as pendentes para o primeiro tópico de retry; retorna quantas.</summary>
    public async Task<int> ReplayAsync(CancellationToken ct)
    {
        if (options.RetryDelays.Count == 0)
        {
            throw new InvalidOperationException("sem tópicos de retry para reprocessar");
        }
        await _replayLock.WaitAsync(ct);
        try
        {
            var replayed = 0;
            await ReadPendingAsync(int.MaxValue, async (result, token) =>
            {
                var headers = new Headers();
                foreach (var h in result.Message.Headers ?? new Headers())
                {
                    headers.Add(h.Key, h.GetValueBytes());
                }
                FailureHeaders.Set(headers, FailureHeaders.RetryAtKey,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
                await producer.ProduceAsync(options.RetryTopic(1), result.Message.Key, result.Message.Value, headers, token);
                replayed++;
            }, commit: true, ct);
            if (replayed > 0)
            {
                LogReplayed(logger, replayed, options.DeadLetterTopic);
            }
            return replayed;
        }
        finally
        {
            _replayLock.Release();
        }
    }

    /// <summary>
    /// Lê da posição do grupo de reprocessamento até o fim de cada partição (o fim no momento da
    /// leitura: o que chega durante não entra). Com commit, avança a posição até onde leu.
    /// </summary>
    private Task ReadPendingAsync(int limit, Func<ConsumeResult<string, string>, CancellationToken, Task> onEach, bool commit, CancellationToken ct) =>
        Task.Run(async () =>
        {
            await producer.EnsureTopicsAsync(ct);
            using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
            {
                BootstrapServers = options.BootstrapServers,
                GroupId = ReplayGroup,
                EnableAutoCommit = false,
                AutoOffsetReset = AutoOffsetReset.Earliest,
            }).Build();

            var partitions = PartitionsOf(options.DeadLetterTopic);
            var committed = consumer.Committed(partitions, s_kafkaTimeout).ToDictionary(c => c.TopicPartition, c => c.Offset);
            var ends = new Dictionary<TopicPartition, long>();
            var starts = new List<TopicPartitionOffset>();
            foreach (var tp in partitions)
            {
                var marks = consumer.QueryWatermarkOffsets(tp, s_kafkaTimeout);
                var start = committed[tp] is { IsSpecial: false } c && c.Value >= marks.Low.Value ? c.Value : marks.Low.Value;
                if (start < marks.High.Value)
                {
                    ends[tp] = marks.High.Value;
                    starts.Add(new TopicPartitionOffset(tp, start));
                }
            }
            if (starts.Count == 0)
            {
                return;
            }

            consumer.Assign(starts);
            var read = new Dictionary<TopicPartition, TopicPartitionOffset>();
            var count = 0;
            while (ends.Count > 0 && count < limit)
            {
                var result = consumer.Consume(s_kafkaTimeout);
                if (result is null)
                {
                    break; // não chegou nada a tempo: devolve o que leu
                }
                if (!ends.TryGetValue(result.TopicPartition, out var end) || result.Offset.Value >= end)
                {
                    continue;
                }
                await onEach(result, ct);
                count++;
                read[result.TopicPartition] = new TopicPartitionOffset(result.TopicPartition, result.Offset.Value + 1);
                if (result.Offset.Value + 1 >= end)
                {
                    ends.Remove(result.TopicPartition);
                    consumer.Pause([result.TopicPartition]);
                }
            }
            if (commit && read.Count > 0)
            {
                consumer.Commit(read.Values);
            }
            consumer.Close();
        }, ct);

    private List<TopicPartition> PartitionsOf(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = options.BootstrapServers }).Build();
        var metadata = admin.GetMetadata(topic, s_kafkaTimeout).Topics.Single();
        if (metadata.Error.IsError)
        {
            throw new KafkaException(metadata.Error);
        }
        return [.. metadata.Partitions.Select(p => new TopicPartition(topic, p.PartitionId))];
    }

    private static DeadLetter Describe(ConsumeResult<string, string> result)
    {
        var headers = result.Message.Headers;
        var envelope = EventEnvelope.TryParse(result.Message.Value);
        return new DeadLetter(
            envelope?.EventId,
            envelope?.EventType,
            FailureHeaders.OriginalTopic(headers) ?? "?",
            int.TryParse(FailureHeaders.Get(headers, FailureHeaders.OriginalPartition), CultureInfo.InvariantCulture, out var p) ? p : null,
            long.TryParse(FailureHeaders.Get(headers, FailureHeaders.OriginalOffset), CultureInfo.InvariantCulture, out var o) ? o : null,
            int.TryParse(FailureHeaders.Get(headers, FailureHeaders.Attempts), CultureInfo.InvariantCulture, out var a) ? a : 0,
            FailureHeaders.Get(headers, FailureHeaders.Error),
            DateTimeOffset.TryParse(FailureHeaders.Get(headers, FailureHeaders.FailedAt), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var at) ? at : null);
    }

    public void Dispose() => _replayLock.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} mensagens de {DeadLetterTopic} devolvidas para reprocessamento")]
    private static partial void LogReplayed(ILogger logger, int count, string deadLetterTopic);
}
