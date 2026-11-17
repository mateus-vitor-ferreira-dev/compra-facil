using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Fulfillment.Application;
using CompraFacil.Fulfillment.Application.Shipping;
using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;
using CompraFacil.Fulfillment.Infrastructure;
using CompraFacil.Fulfillment.Infrastructure.Supplier;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Fulfillment.IntegrationTests;

public sealed class ManualSupplierFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    public TestIdentity Identity { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _postgres.ConnectionString)
            .UseSetting("Kafka:Enabled", "false")
            .UseSetting("Supplier:Mode", "manual")
            .UseSetting("Supplier:DispatcherEnabled", "false")
            .UseSetting("Supplier:CatalogSyncEnabled", "false")
            .UseSetting("Supplier:ReturnsEnabled", "false")
            .UseSetting("Invoice:Enabled", "false")
            .UseSetting("Invoice:ReturnEnabled", "false")
            .UseSetting("Invoice:CancellerEnabled", "false")
            .UseSetting("Auth:Jwks", Identity.Jwks));
        using var _ = Factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
        Identity.Dispose();
    }
}

/// <summary>O fornecedor manual (#412): a planilha pelo painel, o frete da tabela e o pedido pago na fila da equipe.</summary>
public sealed class ManualSupplierTests(ManualSupplierFixture fx) : IClassFixture<ManualSupplierFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal const string Sheet = "sku;nome;categoria;custo;estoque;peso_g;foto\n"
        + "fon-bt-01;Fone Bluetooth;fones-over-ear;89,90;25;250;https://exemplo.com/fone.jpg\n"
        + "\"PAN-22\";\"Frigideira 22 cm; antiaderente\";frigideiras;R$ 1.049,00;3;;\n";

    private Task<HttpResponseMessage> UploadAsync(string csv, string? token) => UploadAsync(fx, csv, token);

    internal static async Task<HttpResponseMessage> UploadAsync(ManualSupplierFixture fx, string csv, string? token)
    {
        using var client = fx.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/supplier-catalog/sheet")
        {
            Content = new StringContent(csv, Encoding.UTF8, "text/csv"),
        };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await client.SendAsync(request, Ct);
    }

    private async Task<List<OutboxMessage>> CatalogEventsAsync()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var events = await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().Set<OutboxMessage>()
            .Where(m => m.Topic == SupplierCatalogEvents.Topic && m.EventType == SupplierCatalogEvents.SupplierProductUpdated)
            .OrderBy(m => m.Id).ToListAsync(Ct);
        foreach (var e in events)
        {
            ContractAssert.AssertEvent("fulfillment.v1.yaml", e.EventType, e.Payload);
        }
        return events;
    }

    [Fact]
    public async Task Planilha_entra_pela_sincronizacao_de_sempre_e_linha_com_erro_nao_muda_nada()
    {
        // a planilha com erro: nada entra, e cada linha diz o que está errado
        using var bad = await UploadAsync("sku;nome;categoria;custo;estoque\nX-1;Mouse;mouses;doze;5\nX-1;Mouse;mouses;12;-1\n", fx.Identity.AdminToken());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        Assert.Equal("application/problem+json", bad.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await bad.Content.ReadAsStringAsync(Ct));
        var lines = problem.RootElement.GetProperty("sheet_errors").EnumerateArray().Select(e => e.GetProperty("line").GetInt32()).ToList();
        Assert.Equal([2, 3, 3], lines);

        // só o operador sobe; o auditor vê o resumo
        using var auditor = await UploadAsync(Sheet, fx.Identity.AuditorToken());
        Assert.Equal(HttpStatusCode.Forbidden, auditor.StatusCode);

        using var ok = await UploadAsync(Sheet, fx.Identity.AdminToken());
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using var body = JsonDocument.Parse(await ok.Content.ReadAsStringAsync(Ct));
        Assert.Equal(2, body.RootElement.GetProperty("products").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("sync").GetProperty("created").GetInt32());

        var events = await CatalogEventsAsync();
        var fone = events.Select(e => EventEnvelope.TryParse(e.Payload)!.DataAs<SupplierProductUpdatedData>()).Single(d => d.Vid == "FON-BT-01");
        Assert.Equal(("manual", "fones-over-ear", 8990L, "BRL", 250), (fone.Supplier, fone.Category, fone.Cost.AmountCents, fone.Cost.Currency, fone.WeightGrams!.Value));

        // a mesma planilha de novo não muda nada; sem uma linha, o produto sai da loja
        using var again = await UploadAsync(Sheet, fx.Identity.AdminToken());
        using var againBody = JsonDocument.Parse(await again.Content.ReadAsStringAsync(Ct));
        Assert.Equal(2, againBody.RootElement.GetProperty("sync").GetProperty("unchanged").GetInt32());
        using var fewer = await UploadAsync(string.Join('\n', Sheet.Split('\n').Take(2)), fx.Identity.AdminToken());
        using var fewerBody = JsonDocument.Parse(await fewer.Content.ReadAsStringAsync(Ct));
        Assert.Equal(1, fewerBody.RootElement.GetProperty("sync").GetProperty("removed").GetInt32());

        using var client = fx.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fx.Identity.AuditorToken());
        var summary = await client.GetFromJsonAsync<JsonElement>("/v1/admin/supplier-catalog/sheet", Ct);
        Assert.True(summary.GetProperty("enabled").GetBoolean());
        Assert.Equal(1, summary.GetProperty("products").GetInt32());
        var template = await client.GetStringAsync("/v1/admin/supplier-catalog/sheet/template", Ct);
        Assert.StartsWith("sku;nome;categoria;custo;estoque", template.TrimStart('﻿'), StringComparison.Ordinal);

        // o estoque guarda a margem de segurança (1 unidade)
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<ISupplierGateway>();
        Assert.Equal(new Dictionary<string, int> { ["FON-BT-01"] = 24, ["NAO-EXISTE"] = 0 },
            await gateway.GetStockAsync(["FON-BT-01", "NAO-EXISTE"], Ct));
    }

}

/// <summary>O pedido pago no fornecedor manual (#412), num banco só dele: a planilha de um teste não atrapalha o outro.</summary>
public sealed class ManualSupplierOrderTests(ManualSupplierFixture fx) : IClassFixture<ManualSupplierFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> UploadAsync(string csv, string? token) => ManualSupplierTests.UploadAsync(fx, csv, token);

    [Fact]
    public async Task Frete_em_reais_pela_tabela_e_pedido_pago_vai_para_a_fila_da_equipe()
    {
        using (var upload = await UploadAsync(ManualSupplierTests.Sheet, fx.Identity.AdminToken()))
        {
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            // 2 fones de 250 g: até 1 kg, o preço base; para SP (CEP 0/1), sem acréscimo, e o prazo do fornecedor somado
            var quotes = await scope.ServiceProvider.GetRequiredService<ShippingQuotes>()
                .QuoteAsync("01310100", [new ShippingItem("FON-BT-01", 2)], Ct);
            var economico = quotes.Single(q => q.Id == "economico");
            Assert.Equal((1990L, 1 + 4 + 2, 1 + 8 + 2), (economico.PriceCents, economico.MinDays, economico.MaxDays));
            Assert.Contains(quotes, q => q.Id == "expresso");
        }

        var id = Guid.NewGuid();
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var handler = ActivatorUtilities.CreateInstance<OnOrderConfirmed>(scope.ServiceProvider);
            var item = new OrderItem(Guid.NewGuid(), "FON-BT-01", "Fone Bluetooth", 1, new Money(15990, "BRL"), new Money(15990, "BRL"));
            await handler.HandleAsync(new OrderConfirmedData(id, Guid.NewGuid(), "ana@exemplo.com", [item], new Money(17980, "BRL"),
                    new Recipient("Ana Souza", "52998224725", "11987654321"),
                    new ShippingAddress("01310100", "Avenida Paulista", "1000", null, "Bela Vista", "São Paulo", "SP"),
                    "pix", new OrderShipping("economico", new Money(1990, "BRL"), new DateOnly(2026, 12, 28))),
                new EventContext(Guid.NewGuid(), FulfillmentEvents.OrderConfirmed, FulfillmentEvents.OrdersTopic, "req", DateTimeOffset.UtcNow), Ct);
            await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().SaveChangesAsync(Ct);
        }

        var dispatcher = ActivatorUtilities.CreateInstance<SupplierDispatcher>(fx.Factory.Services);
        Assert.Equal(1, await dispatcher.DispatchOnceAsync(Ct));
        Assert.Equal(0, await dispatcher.DispatchOnceAsync(Ct)); // na fila da equipe, o despachante não pega de novo

        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>();
            var order = await db.SupplierOrders.AsNoTracking().SingleAsync(o => o.Id == id, Ct);
            Assert.Equal((SupplierOrderStatus.AwaitingStaff, "manual"), (order.Status, order.Supplier));
            Assert.NotNull(order.AwaitingStaffSince);
            Assert.Null(order.SupplierOrderId);
            Assert.Equal((8990L, 1990L), (order.ProductCostCents!.Value, order.FreightCostCents!.Value));
            // para o cliente, o pedido segue pago: nenhum evento do fornecedor
            Assert.False(await db.Set<OutboxMessage>().AnyAsync(m => m.MessageKey == id.ToString() && m.Topic == FulfillmentEvents.Topic, Ct));
        }

        // a equipe cancela pelo painel antes de fazer o pedido no fornecedor: o pedido falha e o cliente é estornado
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            Assert.Equal(SupplierOrderActionResult.Done, await scope.ServiceProvider.GetRequiredService<SupplierOrderActions>().CancelAsync(id, Ct));
        }
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>();
            var order = await db.SupplierOrders.AsNoTracking().SingleAsync(o => o.Id == id, Ct);
            Assert.Equal((SupplierOrderStatus.Failed, SupplierFailureReasons.StoreCancelled), (order.Status, order.FailureReason));
        }
    }
}

/// <summary>No fornecedor manual ninguém manda webhook: a rota não existe, nem com a assinatura do simulador.</summary>
public sealed class ManualSupplierWebhookTests(ManualSupplierFixture fx) : IClassFixture<ManualSupplierFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Webhook_IsNotFound_EvenSignedWithTheSimulatorSecret()
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            messageId = $"m-{Guid.NewGuid():N}",
            type = "ORDER",
            @params = new { orderNumber = Guid.NewGuid().ToString(), orderStatus = "CANCELLED" },
        });
        var signature = "sha256=" + Convert.ToHexStringLower(
            System.Security.Cryptography.HMACSHA256.HashData(Encoding.UTF8.GetBytes("sim-secret"), body));
        using var client = fx.Factory.CreateClient();
        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.Add("X-Sim-Signature", signature);

        using var rejected = new MetricRecorder(FulfillmentMetrics.MeterName, "compra_facil.fulfillment.webhooks_rejected");

        using var response = await client.PostAsync(new Uri("/v1/webhooks/supplier", UriKind.Relative), content, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(rejected.Total(("reason", "manual_mode")) >= 1); // o alerta "Webhook recusado em rajada"
    }
}
