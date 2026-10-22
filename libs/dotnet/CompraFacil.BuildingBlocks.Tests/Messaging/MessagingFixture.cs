using System.Collections.Concurrent;
using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redpanda;

namespace CompraFacil.BuildingBlocks.Tests.Messaging;

public sealed record ThingCreated(Guid ThingId, string Name);

public sealed record ThingSeen(Guid ThingId);

public sealed class SeenThing
{
    public Guid Id { get; init; }
}

/// <summary>DbContext de um "serviço" de teste: uma tabela própria + outbox e inbox.</summary>
public sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SeenThing>().ToTable("seen_things");
        modelBuilder.AddMessagingTables();
    }
}

/// <summary>Registra o que o handler recebeu e controla falhas simuladas.</summary>
public sealed class Recorder
{
    public ConcurrentBag<(ThingCreated Data, EventContext Context)> Calls { get; } = [];
    public ConcurrentDictionary<Guid, int> FailuresLeft { get; } = new();
}

/// <summary>Grava uma linha, publica ThingSeen (sem handler: testa o skip) e registra a chamada.</summary>
public sealed class ThingCreatedHandler(TestDb db, IOutbox outbox, Recorder recorder) : IHandleEvent<ThingCreated>
{
    public Task HandleAsync(ThingCreated data, EventContext context, CancellationToken ct)
    {
        if (recorder.FailuresLeft.TryGetValue(data.ThingId, out var left) && left > 0)
        {
            recorder.FailuresLeft[data.ThingId] = left - 1;
            throw new InvalidOperationException("falha simulada");
        }
        db.Add(new SeenThing { Id = data.ThingId });
        outbox.Add(MessagingFixture.Topic, data.ThingId.ToString(), "ThingSeen", new ThingSeen(data.ThingId));
        recorder.Calls.Add((data, context));
        return Task.CompletedTask;
    }
}

/// <summary>Postgres + Redpanda reais (mesmas imagens do compose) e um host com relay e consumer.</summary>
public sealed class MessagingFixture : IAsyncLifetime
{
    public const string Topic = "test.thing.v1";
    public const string Service = "test-svc";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.11-alpine").Build();
    private readonly RedpandaContainer _redpanda = new RedpandaBuilder("redpandadata/redpanda:v26.2.5").Build();

    public IHost Host { get; private set; } = null!;
    public Recorder Recorder => Host.Services.GetRequiredService<Recorder>();
    public string Brokers => _redpanda.GetBootstrapAddress();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redpanda.StartAsync());
        Host = BuildHost(Brokers, enabled: true);
        await using (var scope = Host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TestDb>().Database.EnsureCreatedAsync();
        }
        await Host.StartAsync();
    }

    /// <summary>Host do "serviço" de teste; com enabled=false, sem relay nem consumer rodando.</summary>
    public IHost BuildHost(string brokers, bool enabled, TimeSpan? publishTimeout = null, string? connectionString = null)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString ?? _postgres.GetConnectionString(),
            ["Kafka:BootstrapServers"] = brokers,
            ["Kafka:PollInterval"] = "00:00:00.050",
            ["Kafka:MaxBackoff"] = "00:00:00.500",
            ["Kafka:PublishTimeout"] = (publishTimeout ?? TimeSpan.FromSeconds(10)).ToString(),
            ["Kafka:Enabled"] = enabled.ToString(),
        });
        builder.Services.AddSingleton<Recorder>();
        builder.AddPostgres<TestDb>();
        builder.AddMessaging<TestDb>(Service, m => m
            .Produces(Topic)
            .Handle<ThingCreated, ThingCreatedHandler>(Topic, "ThingCreated"));
        return builder.Build();
    }

    /// <summary>Banco novo no mesmo Postgres: outbox isolado do host principal.</summary>
    public async Task<string> CreateDatabaseAsync(string name)
    {
        await using (var conn = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {name}", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (Host is not null)
        {
            await Host.StopAsync();
            Host.Dispose();
        }
        await _postgres.DisposeAsync();
        await _redpanda.DisposeAsync();
    }
}
