using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Inventory.Application.Stock;
using CompraFacil.Inventory.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Inventory.IntegrationTests;

/// <summary>Postgres real + o inventory em memória (sem Kafka: os eventos são conferidos no outbox).</summary>
public sealed class InventoryFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    /// <summary>O identity dos testes: as rotas admin validam os tokens dele pelo JWKS (Auth:Jwks).</summary>
    public TestIdentity Identity { get; } = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _postgres.ConnectionString)
            .UseSetting("Kafka:Enabled", "false")
            .UseSetting("Inventory:InitialOnHandForNewProducts", "7")
            .UseSetting("Auth:Jwks", Identity.Jwks));
        using var _ = Factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
        Identity.Dispose();
    }

    private static readonly DateTimeOffset s_now = DateTimeOffset.UtcNow;

    /// <summary>Roda um handler como o EventConsumer faz: um escopo por mensagem.</summary>
    public async Task HandleAsync<TData, THandler>(TData data) where THandler : IHandleEvent<TData>
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);
        await handler.HandleAsync(data, new EventContext(Guid.NewGuid(), typeof(TData).Name, "orders.order.v1", "req-test", s_now),
            TestContext.Current.CancellationToken);
        await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Produto com saldo: criado pelo evento do catálogo e ajustado até onHand.</summary>
    public async Task<Guid> ProductWithStockAsync(int onHand)
    {
        var id = Guid.NewGuid();
        await HandleAsync<ProductCreatedData, ProductCreated>(new ProductCreatedData(id)); // nasce com 7
        if (onHand != 7)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AdjustStock>().HandleAsync(
                new AdjustStockCommand(id, onHand - 7, "inventory_count", null, Guid.NewGuid().ToString(), "teste"),
                TestContext.Current.CancellationToken);
        }
        return id;
    }

    public async Task<T> InDbAsync<T>(Func<InventoryDbContext, Task<T>> query)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<InventoryDbContext>());
    }

    public Task<List<OutboxMessage>> EventsForAsync(Guid orderId) =>
        InDbAsync(db => db.Set<OutboxMessage>().Where(m => m.MessageKey == orderId.ToString()).OrderBy(m => m.Id)
            .ToListAsync(TestContext.Current.CancellationToken));

    /// <summary>Token de operador do identity (de teste); outros papéis para testar o 403.</summary>
    public string AdminToken(string[]? roles = null) => Identity.Token(roles ?? ["admin"]);
}
