using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;
using CompraFacil.Fulfillment.Infrastructure;
using CompraFacil.Fulfillment.Infrastructure.Supplier;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompraFacil.Fulfillment.IntegrationTests;

/// <summary>O fornecedor dos testes: responde o que o teste mandar, e conta as chamadas.</summary>
public sealed class FakeSupplier : ISupplierGateway
{
    public Func<SupplierOrderRequest, SupplierPlacement> Respond { get; set; } = r => new SupplierPlacement.Accepted("CJ" + r.OrderNumber.ToString("N")[..10]);
    public List<SupplierOrderRequest> Calls { get; } = [];
    public string Name => "fake";

    /// <summary>O feed que o fornecedor devolve; nulo = fora do ar.</summary>
    public List<SupplierFeedItem>? Feed { get; set; } = [];

    public Task<SupplierPlacement> PlaceOrderAsync(SupplierOrderRequest request, CancellationToken ct)
    {
        Calls.Add(request);
        return Task.FromResult(Respond(request));
    }

    public Task<IReadOnlyList<SupplierFeedItem>> ListProductsAsync(CancellationToken ct) =>
        Feed is { } feed
            ? Task.FromResult<IReadOnlyList<SupplierFeedItem>>([.. feed])
            : throw new SupplierUnavailableException("fornecedor fora do ar");
}

public sealed class FulfillmentFixture : IAsyncLifetime
{
    public const string Secret = "segredo-dos-testes";
    private readonly PostgresFixture _postgres = new();
    public TestIdentity Identity { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public FakeSupplier Supplier { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _postgres.ConnectionString)
            .UseSetting("Kafka:Enabled", "false")
            .UseSetting("Supplier:DispatcherEnabled", "false")
            .UseSetting("Supplier:CatalogSyncEnabled", "false")
            .UseSetting("Supplier:WebhookSecret", Secret)
            .UseSetting("Auth:Jwks", Identity.Jwks)
            .ConfigureTestServices(s =>
            {
                s.RemoveAll<ISupplierGateway>();
                s.AddSingleton<ISupplierGateway>(Supplier);
            }));
        using var _ = Factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
        Identity.Dispose();
    }
}

/// <summary>O pedido pago até o fornecedor e de volta: fila, despachante, webhook e os eventos.</summary>
public sealed class FulfillmentTests(FulfillmentFixture fx) : IClassFixture<FulfillmentFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task ConfirmedAsync(Guid orderId, string email = "ana@exemplo.com", DateTimeOffset? confirmedAt = null, OrderCutoff? cutoff = null)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var handler = cutoff is null
            ? ActivatorUtilities.CreateInstance<OnOrderConfirmed>(scope.ServiceProvider)
            : ActivatorUtilities.CreateInstance<OnOrderConfirmed>(scope.ServiceProvider, cutoff);
        var item = new OrderItem(Guid.NewGuid(), "MOU-000001", "Mouse sem Fio", 2, new Money(9990, "BRL"), new Money(19980, "BRL"));
        await handler.HandleAsync(new OrderConfirmedData(orderId, Guid.NewGuid(), email, [item], new Money(19980, "BRL")),
            new EventContext(Guid.NewGuid(), FulfillmentEvents.OrderConfirmed, FulfillmentEvents.OrdersTopic, "req", confirmedAt ?? DateTimeOffset.UtcNow), Ct);
        await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().SaveChangesAsync(Ct);
    }

    private async Task<int> DispatchAsync()
    {
        var dispatcher = ActivatorUtilities.CreateInstance<SupplierDispatcher>(fx.Factory.Services);
        return await dispatcher.DispatchOnceAsync(Ct);
    }

    private async Task<SupplierOrder> OrderAsync(Guid id)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().SupplierOrders.AsNoTracking().SingleAsync(o => o.Id == id, Ct);
    }

    /// <summary>Os eventos do pedido, na ordem, cada um conferido contra o contrato.</summary>
    private async Task<List<string>> EventsOfAsync(Guid id)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var events = await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().Set<OutboxMessage>()
            .Where(m => m.MessageKey == id.ToString()).OrderBy(m => m.Id).ToListAsync(Ct);
        foreach (var e in events)
        {
            ContractAssert.AssertEvent("fulfillment.v1.yaml", e.EventType, e.Payload);
        }
        return [.. events.Select(e => e.EventType)];
    }

    private async Task<(HttpStatusCode Status, string Body)> WebhookAsync(object body, string? secret = FulfillmentFixture.Secret)
    {
        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        if (secret is not null)
        {
            var hmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(json));
            content.Headers.Add("X-Sim-Signature", "sha256=" + Convert.ToHexStringLower(hmac));
        }
        using var client = fx.Factory.CreateClient();
        using var response = await client.PostAsync("/v1/webhooks/supplier", content, Ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private static object Update(Guid orderId, string status, string? track = null, string? messageId = null) => new
    {
        messageId = messageId ?? Guid.NewGuid().ToString(),
        type = "ORDER",
        messageType = "UPDATE",
        @params = new
        {
            orderId = "CJ0000000001",
            orderNumber = orderId.ToString(),
            orderStatus = status,
            trackNumber = track,
            trackingProvider = track is null ? null : "Correios",
            trackingUrl = track is null ? null : $"https://t.17track.net/en#nums={track}",
            reason = (string?)null,
        },
    };

    [Fact]
    public async Task Pedido_pago_vai_ao_fornecedor_uma_vez_e_volta_entregue()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);
        await ConfirmedAsync(id); // o mesmo evento de novo não cria outro pedido
        Assert.Equal(SupplierOrderStatus.Pending, (await OrderAsync(id)).Status);

        await DispatchAsync();
        var placed = await OrderAsync(id);
        Assert.Equal(SupplierOrderStatus.Placed, placed.Status);
        var call = Assert.Single(fx.Supplier.Calls, c => c.OrderNumber == id);
        Assert.Equal("MOU-000001", Assert.Single(call.Items).Sku);

        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(Update(id, "UNSHIPPED"))).Status);
        var shipped = Update(id, "SHIPPED", "BR123456789CJ", messageId: "msg-enviado-" + id);
        Assert.Contains("applied", (await WebhookAsync(shipped)).Body);
        Assert.Contains("duplicate", (await WebhookAsync(shipped)).Body); // o fornecedor repete
        Assert.Contains("applied", (await WebhookAsync(Update(id, "DELIVERED"))).Body);
        // atrasado: o aceite chega depois da entrega e não faz voltar
        Assert.Contains("ignored", (await WebhookAsync(Update(id, "UNSHIPPED"))).Body);

        var delivered = await OrderAsync(id);
        Assert.Equal(SupplierOrderStatus.Delivered, delivered.Status);
        Assert.Equal("BR123456789CJ", delivered.Tracking!.Number);
        Assert.Equal(
            [FulfillmentEvents.SupplierOrderPlaced, FulfillmentEvents.SupplierOrderShipped, FulfillmentEvents.SupplierOrderDelivered],
            await EventsOfAsync(id));
    }

    [Fact]
    public async Task Pedido_confirmado_antes_do_corte_nao_vai_ao_fornecedor()
    {
        var cutoff = new OrderCutoff(DateTimeOffset.UtcNow);
        var old = Guid.NewGuid();
        var fresh = Guid.NewGuid();

        await ConfirmedAsync(old, confirmedAt: cutoff.Since!.Value.AddDays(-2), cutoff: cutoff);
        await ConfirmedAsync(fresh, confirmedAt: cutoff.Since.Value.AddSeconds(1), cutoff: cutoff);

        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>();
        Assert.False(await db.SupplierOrders.AnyAsync(o => o.Id == old, Ct));
        Assert.True(await db.SupplierOrders.AnyAsync(o => o.Id == fresh, Ct));
    }

    [Fact]
    public async Task Recusa_do_fornecedor_vira_falha_sem_nova_tentativa()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);
        fx.Supplier.Respond = r => r.OrderNumber == id
            ? new SupplierPlacement.Rejected(SupplierFailureReasons.OutOfStock, "Out of stock: MOU-000001")
            : new SupplierPlacement.Accepted("CJ1");

        await DispatchAsync();
        await DispatchAsync();

        var order = await OrderAsync(id);
        Assert.Equal(SupplierOrderStatus.Failed, order.Status);
        Assert.Equal(SupplierFailureReasons.OutOfStock, order.FailureReason);
        Assert.Single(fx.Supplier.Calls, c => c.OrderNumber == id);
        Assert.Equal([FulfillmentEvents.SupplierOrderFailed], await EventsOfAsync(id));
    }

    [Fact]
    public async Task Fornecedor_fora_do_ar_volta_para_a_fila_e_espera()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);
        fx.Supplier.Respond = r => r.OrderNumber == id
            ? throw new SupplierUnavailableException("fornecedor respondeu 503")
            : new SupplierPlacement.Accepted("CJ1");

        await DispatchAsync();
        await DispatchAsync(); // ainda na espera: não tenta de novo

        var order = await OrderAsync(id);
        Assert.Equal(SupplierOrderStatus.Pending, order.Status);
        Assert.Equal(1, order.Attempts);
        Assert.NotNull(order.NextAttemptAt);
        Assert.Single(fx.Supplier.Calls, c => c.OrderNumber == id);
        Assert.Empty(await EventsOfAsync(id));
    }

    [Fact]
    public async Task Cancelado_pelo_fornecedor_depois_de_aceito_vira_falha()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);
        await DispatchAsync();

        await WebhookAsync(Update(id, "CANCELLED"));

        var order = await OrderAsync(id);
        Assert.Equal(SupplierOrderStatus.Failed, order.Status);
        Assert.Equal(SupplierFailureReasons.SupplierCancelled, order.FailureReason);
        Assert.Equal([FulfillmentEvents.SupplierOrderPlaced, FulfillmentEvents.SupplierOrderFailed], await EventsOfAsync(id));
    }

    [Fact]
    public async Task Webhook_sem_a_assinatura_certa_e_recusado()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);

        Assert.Equal(HttpStatusCode.Unauthorized, (await WebhookAsync(Update(id, "SHIPPED", "BR1"), secret: "outro")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WebhookAsync(Update(id, "SHIPPED", "BR1"), secret: null)).Status);
        Assert.Equal(SupplierOrderStatus.Pending, (await OrderAsync(id)).Status);
    }

    [Fact]
    public async Task Webhook_de_pedido_desconhecido_responde_200()
    {
        var (status, body) = await WebhookAsync(Update(Guid.NewGuid(), "SHIPPED", "BR1"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("unknown_order", body);
        ContractAssert.AssertSchema("fulfillment.v1.yaml", "/components/schemas/SupplierWebhookAck", body);
    }
}
