using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CompraFacil.BuildingBlocks.Tests.Messaging;

/// <summary>Outbox → relay → Kafka → consumer → inbox, com Postgres e Redpanda reais.</summary>
public sealed class MessagingTests(MessagingFixture fx) : IClassFixture<MessagingFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> PublishThingAsync(Guid thingId, string correlationId, string? key = null)
    {
        var previous = CorrelationContext.Current;
        CorrelationContext.Current = new CorrelationContext(correlationId, null);
        try
        {
            await using var scope = fx.Host.Services.CreateAsyncScope();
            var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
            var eventId = outbox.Add(MessagingFixture.Topic, key ?? thingId.ToString(), "ThingCreated", new ThingCreated(thingId, "coisa"));
            await scope.ServiceProvider.GetRequiredService<TestDb>().SaveChangesAsync(Ct);
            return eventId;
        }
        finally
        {
            CorrelationContext.Current = previous;
        }
    }

    private int CallsFor(Guid thingId) => fx.Recorder.Calls.Count(c => c.Data.ThingId == thingId);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string because)
    {
        using var timeout = new CancellationTokenSource(s_timeout);
        while (!await condition())
        {
            if (timeout.IsCancellationRequested)
            {
                Assert.Fail($"tempo esgotado: {because}");
            }
            await Task.Delay(50, Ct);
        }
    }

    private async Task<T> InDbAsync<T>(Func<TestDb, Task<T>> query)
    {
        await using var scope = fx.Host.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TestDb>());
    }

    /// <summary>Lê do início todas as mensagens do tópico com essa chave.</summary>
    private List<EventEnvelope> ReadTopic(string key, int expected)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = fx.Brokers,
            GroupId = "leitor-" + Guid.NewGuid(),
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(MessagingFixture.Topic);
        var found = new List<EventEnvelope>();
        var deadline = DateTime.UtcNow + s_timeout;
        while (found.Count < expected && DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result?.Message.Key == key)
            {
                found.Add(EventEnvelope.TryParse(result.Message.Value)!);
            }
        }
        return found;
    }

    [Fact]
    public async Task Event_GoesThroughKafka_AndChainsCorrelation()
    {
        var thingId = Guid.NewGuid();

        var eventId = await PublishThingAsync(thingId, "req-roundtrip");

        await WaitUntilAsync(() => Task.FromResult(CallsFor(thingId) == 1), "handler chamado");
        var (data, context) = fx.Recorder.Calls.Single(c => c.Data.ThingId == thingId);
        Assert.Equal("coisa", data.Name);
        Assert.Equal(eventId, context.EventId);
        Assert.Equal("req-roundtrip", context.CorrelationId);
        Assert.True(await InDbAsync(db => db.Set<SeenThing>().AnyAsync(s => s.Id == thingId, Ct)), "escrita do handler gravada");

        // o ThingSeen publicado pelo handler: mesmo fluxo, causado pelo ThingCreated; sem handler, é pulado
        var envelopes = ReadTopic(thingId.ToString(), expected: 2);
        Assert.Equal(["ThingCreated", "ThingSeen"], envelopes.Select(e => e.EventType));
        var seen = envelopes[1];
        Assert.Equal(eventId, seen.CausationId);
        Assert.Equal("req-roundtrip", seen.CorrelationId);
        Assert.Equal(MessagingFixture.Service, seen.Producer);
        Assert.Null(envelopes[0].CausationId);
        await WaitUntilAsync(async () => await InDbAsync(db => db.Set<OutboxMessage>().CountAsync(Ct)) == 0, "outbox vazio");
    }

    [Fact]
    public async Task Envelope_FollowsTheContract()
    {
        var thingId = Guid.NewGuid();
        await PublishThingAsync(thingId, "req-json");

        var raw = ReadTopic(thingId.ToString(), expected: 1).Single();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(raw, EventEnvelope.JsonOptions));
        var root = json.RootElement;

        string[] fields = ["event_id", "event_type", "event_version", "occurred_at", "producer", "correlation_id", "causation_id", "data"];
        Assert.Equal(fields, root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("causation_id").ValueKind);
        Assert.Equal(1, root.GetProperty("event_version").GetInt32());
        Assert.Equal(thingId, root.GetProperty("data").GetProperty("thing_id").GetGuid());
        Assert.Equal(TimeSpan.Zero, root.GetProperty("occurred_at").GetDateTimeOffset().Offset);
    }

    [Fact]
    public async Task Redelivery_IsIgnoredByTheInbox()
    {
        var thingId = Guid.NewGuid();
        var envelope = new EventEnvelope(Guid.CreateVersion7(), "ThingCreated", 1, DateTimeOffset.UtcNow, "outro", "req-dup", null,
            JsonSerializer.SerializeToElement(new ThingCreated(thingId, "dup"), EventEnvelope.JsonOptions));

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = fx.Brokers }).Build();
        for (var i = 0; i < 2; i++) // o mesmo evento entregue duas vezes (retry do produtor, rebalance)
        {
            await producer.ProduceAsync(MessagingFixture.Topic, new Message<string, string> { Key = thingId.ToString(), Value = envelope.Serialize() }, Ct);
        }
        // um evento depois dos dois, com a mesma chave (mesma partição, ordem garantida): quando ele
        // for processado, as duas entregas já passaram pelo consumer
        var marker = Guid.NewGuid();
        await PublishThingAsync(marker, "req-marker", key: thingId.ToString());

        await WaitUntilAsync(() => Task.FromResult(CallsFor(marker) == 1), "evento seguinte processado");
        Assert.Equal(1, CallsFor(thingId));
        Assert.Equal(1, await InDbAsync(db => db.Set<InboxMessage>().CountAsync(m => m.EventId == envelope.EventId, Ct)));
    }

    [Fact]
    public async Task InvalidMessage_IsSkipped_AndTheNextIsHandled()
    {
        var thingId = Guid.NewGuid();
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = fx.Brokers }).Build();
        await producer.ProduceAsync(MessagingFixture.Topic, new Message<string, string> { Key = thingId.ToString(), Value = "isto não é json" }, Ct);

        await PublishThingAsync(thingId, "req-after-invalid");

        await WaitUntilAsync(() => Task.FromResult(CallsFor(thingId) == 1), "evento depois do inválido processado");
    }

    [Fact]
    public async Task FailingHandler_IsRetried_AndRunsOnce()
    {
        var thingId = Guid.NewGuid();
        fx.Recorder.FailuresLeft[thingId] = 2;

        await PublishThingAsync(thingId, "req-retry");

        await WaitUntilAsync(() => Task.FromResult(CallsFor(thingId) == 1), "processado depois das falhas");
        Assert.Equal(0, fx.Recorder.FailuresLeft[thingId]);
        Assert.Equal(1, await InDbAsync(db => db.Set<SeenThing>().CountAsync(s => s.Id == thingId, Ct)));
    }

    [Fact]
    public async Task KafkaDown_EventsWaitInTheOutbox_AndGoOutLater()
    {
        // outbox num banco próprio: o relay do host principal não o enxerga
        var connectionString = await fx.CreateDatabaseAsync("kafka_down");
        var thingId = Guid.NewGuid();
        using (var writer = fx.BuildHost(fx.Brokers, enabled: false, connectionString: connectionString))
        {
            await using var scope = writer.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TestDb>();
            await db.Database.EnsureCreatedAsync(Ct);
            scope.ServiceProvider.GetRequiredService<IOutbox>()
                .Add(MessagingFixture.Topic, thingId.ToString(), "ThingCreated", new ThingCreated(thingId, "sem kafka"));
            await db.SaveChangesAsync(Ct);
        }

        // 1. broker inexistente: a publicação falha e o evento continua no outbox
        using (var down = fx.BuildHost("127.0.0.1:1", enabled: false, publishTimeout: TimeSpan.FromSeconds(2), connectionString: connectionString))
        {
            using var relay = ActivatorUtilities.CreateInstance<OutboxRelay<TestDb>>(down.Services);
            await Assert.ThrowsAnyAsync<KafkaException>(() => relay.DrainOnceAsync(Ct));
            await using var scope = down.Services.CreateAsyncScope();
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<TestDb>().Set<OutboxMessage>().CountAsync(Ct));
        }

        // 2. broker de volta: o mesmo evento sai, e o consumer do host principal o processa uma vez
        using (var up = fx.BuildHost(fx.Brokers, enabled: false, connectionString: connectionString))
        {
            using var relay = ActivatorUtilities.CreateInstance<OutboxRelay<TestDb>>(up.Services);
            Assert.Equal(1, await relay.DrainOnceAsync(Ct));
            await using var scope = up.Services.CreateAsyncScope();
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<TestDb>().Set<OutboxMessage>().CountAsync(Ct));
        }
        await WaitUntilAsync(() => Task.FromResult(CallsFor(thingId) == 1), "evento publicado depois que o broker voltou");
    }
}
