using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Domain;
using CompraFacil.Fulfillment.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Fulfillment.IntegrationTests;

/// <summary>A disponibilidade do fornecedor espelhada (#218): no ciclo do feed, pelo webhook e na conferência do pedido.</summary>
public sealed class SupplierStockTests(FulfillmentFixture fx) : IClassFixture<FulfillmentFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SupplierFeedItem Item(string vid) => new(vid, "CJ" + vid, "Item " + vid, "Mouse", 520, "USD", null, null);

    private async Task SyncAsync()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CatalogSync>().RunAsync(Ct);
    }

    /// <summary>Os SupplierStockChanged gravados, na ordem, validados no contrato: (vid, disponível).</summary>
    private async Task<List<(string Vid, int Available)>> StockEventsAsync()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var events = await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().Set<OutboxMessage>()
            .Where(m => m.EventType == SupplierCatalogEvents.SupplierStockChanged).OrderBy(m => m.Id).ToListAsync(Ct);
        return [.. events.Select(e =>
        {
            ContractAssert.AssertEvent("fulfillment.v1.yaml", e.EventType, e.Payload);
            var d = EventEnvelope.TryParse(e.Payload)!.DataAs<SupplierStockChangedData>();
            return (d.Vid, d.Available);
        })];
    }

    private async Task<(HttpStatusCode Status, string Body)> WebhookAsync(object body)
    {
        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var hmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(FulfillmentFixture.Secret), Encoding.UTF8.GetBytes(json));
        content.Headers.Add("X-Sim-Signature", "sha256=" + Convert.ToHexStringLower(hmac));
        using var client = fx.Factory.CreateClient();
        using var response = await client.PostAsync("/v1/webhooks/supplier", content, Ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private static object Stock(string vid, int n, DateTimeOffset at, string? messageId = null) => new
    {
        messageId = messageId ?? Guid.NewGuid().ToString(),
        type = "STOCK",
        messageType = "UPDATE",
        @params = new { vid, storageNum = n, updateDate = at.ToString("O") },
    };

    [Fact]
    public async Task Stock_IsMirrored_FromTheFeedTheWebhookAndTheOrderCheck()
    {
        // 1. o ciclo do feed lê a disponibilidade de cada variante e publica a primeira leitura
        fx.Supplier.Feed = [Item("S-1"), Item("S-2")];
        fx.Supplier.Stock = new() { ["S-1"] = 10, ["S-2"] = 0 };
        await SyncAsync();
        Assert.Equal([("S-1", 10), ("S-2", 0)], (await StockEventsAsync()).OrderBy(e => e.Vid));

        await SyncAsync(); // igual: nada novo
        Assert.Equal(2, (await StockEventsAsync()).Count);

        // 2. estoque fora do ar: o catálogo segue, a disponibilidade fica como estava
        fx.Supplier.Stock = null;
        fx.Supplier.Feed = [Item("S-1"), Item("S-2"), Item("S-3")];
        await SyncAsync();
        Assert.Equal(2, (await StockEventsAsync()).Count);

        // 3. o webhook de estoque: aplicado, repetido e atrasado
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid().ToString();
        var (status, body) = await WebhookAsync(Stock("S-1", 0, now, messageId));
        Assert.Equal((HttpStatusCode.OK, "applied"), (status, JsonDocument.Parse(body).RootElement.GetProperty("result").GetString()));
        (_, body) = await WebhookAsync(Stock("S-1", 0, now, messageId));
        Assert.Equal("duplicate", JsonDocument.Parse(body).RootElement.GetProperty("result").GetString());
        await WebhookAsync(Stock("S-1", 99, now.AddMinutes(-5))); // mais velho que o guardado
        Assert.Equal(("S-1", 0), (await StockEventsAsync())[^1]);
        Assert.Equal(3, (await StockEventsAsync()).Count);

        // o webhook traz a hora em segundos: não perde para uma leitura feita antes dele, no mesmo segundo
        DateTimeOffset readAt;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var product = await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().Set<SupplierProduct>()
                .SingleAsync(p => p.Vid == "S-2", Ct);
            readAt = product.StockCheckedAt!.Value;
        }
        await WebhookAsync(Stock("S-2", 5, new DateTimeOffset(readAt.Ticks - (readAt.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero)));
        Assert.Equal(("S-2", 5), (await StockEventsAsync())[^1]);

        // 4. a conferência do pedido: lê no fornecedor agora e espelha
        fx.Supplier.Stock = new() { ["S-1"] = 4, ["S-3"] = 7 };
        using var client = fx.Factory.CreateClient();
        using var check = await client.PostAsJsonAsync("/v1/internal/supplier-stock/check",
            new { items = new[] { new { vid = "S-1", quantity = 2 }, new { vid = "S-3", quantity = 1 }, new { vid = "NAO-EXISTE", quantity = 1 } } }, Ct);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var json = await check.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("fulfillment.v1.yaml",
            "/paths/~1v1~1internal~1supplier-stock~1check/post/responses/200/content/application~1json/schema", json);
        var lines = JsonDocument.Parse(json).RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(l => l.GetProperty("vid").GetString()!, l => (l.GetProperty("available").GetInt32(), l.GetProperty("known").GetBoolean()));
        Assert.Equal((4, true), lines["S-1"]);
        Assert.Equal((7, true), lines["S-3"]);
        Assert.False(lines["NAO-EXISTE"].Item2);
        Assert.Contains(("S-1", 4), await StockEventsAsync());
        Assert.Contains(("S-3", 7), await StockEventsAsync());

        fx.Supplier.Stock = null;
        using var down = await client.PostAsJsonAsync("/v1/internal/supplier-stock/check", new { items = new[] { new { vid = "S-1", quantity = 1 } } }, Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
        using var invalid = await client.PostAsJsonAsync("/v1/internal/supplier-stock/check", new { items = Array.Empty<object>() }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
    }
}
