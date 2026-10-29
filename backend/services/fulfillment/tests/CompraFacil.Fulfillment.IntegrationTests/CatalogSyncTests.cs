using System.Net;
using System.Net.Http.Headers;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;
using CompraFacil.Fulfillment.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Fulfillment.IntegrationTests;

/// <summary>O feed do fornecedor virando eventos: só o que mudou, e nada some por erro do fornecedor.</summary>
public sealed class CatalogSyncTests(FulfillmentFixture fx) : IClassFixture<FulfillmentFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SupplierFeedItem Item(string vid, long cents = 1240, string category = "Headphones") =>
        new(vid, "CJ" + vid, "Wireless Headphones " + vid, category, cents, "USD", "https://img/" + vid + ".png", 210);

    private async Task<CatalogSyncResult> SyncAsync()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogSync>().RunAsync(Ct);
    }

    /// <summary>Os eventos do catálogo do fornecedor gravados no outbox, na ordem, validados no contrato.</summary>
    private async Task<List<OutboxMessage>> EventsAsync()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var events = await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().Set<OutboxMessage>()
            .Where(m => m.Topic == SupplierCatalogEvents.Topic).OrderBy(m => m.Id).ToListAsync(Ct);
        foreach (var e in events)
        {
            ContractAssert.AssertEvent("fulfillment.v1.yaml", e.EventType, e.Payload);
        }
        return events;
    }

    [Fact]
    public async Task Publishes_OnlyWhatChanged_AndNeverEmptiesTheCatalogByMistake()
    {
        fx.Supplier.Feed = [Item("V-1"), Item("V-2", 3290)];
        Assert.Equal(new CatalogSyncResult(2, 0, 0, 0), await SyncAsync());
        var first = await EventsAsync();
        Assert.Equal(2, first.Count);
        var data = EventEnvelope.TryParse(first[0].Payload)!.DataAs<SupplierProductUpdatedData>();
        Assert.Equal(("V-1", 1240L, "USD", "Headphones"), (data.Vid, data.Cost.AmountCents, data.Cost.Currency, data.Category));

        Assert.Equal(new CatalogSyncResult(0, 0, 0, 2), await SyncAsync()); // nada mudou: nada publicado
        Assert.Equal(2, (await EventsAsync()).Count);

        fx.Supplier.Feed = [Item("V-1", 1500), Item("V-2", 3290)]; // o fornecedor reajustou o V-1
        Assert.Equal(new CatalogSyncResult(0, 1, 0, 1), await SyncAsync());

        fx.Supplier.Feed = [Item("V-1", 1500)]; // o V-2 saiu do feed
        Assert.Equal(new CatalogSyncResult(0, 0, 1, 1), await SyncAsync());
        var removed = (await EventsAsync())[^1];
        Assert.Equal(SupplierCatalogEvents.SupplierProductRemoved, removed.EventType);
        Assert.Equal("V-2", removed.MessageKey);

        fx.Supplier.Feed = []; // feed vazio é erro do fornecedor, não "parou de vender tudo"
        await Assert.ThrowsAsync<SupplierUnavailableException>(SyncAsync);
        fx.Supplier.Feed = null; // fora do ar
        await Assert.ThrowsAsync<SupplierUnavailableException>(SyncAsync);
        Assert.Equal(4, (await EventsAsync()).Count);

        fx.Supplier.Feed = [Item("V-1", 1500), Item("V-2", 3290)]; // o V-2 voltou
        Assert.Equal(new CatalogSyncResult(0, 1, 0, 1), await SyncAsync());
        Assert.Equal(SupplierCatalogEvents.SupplierProductUpdated, (await EventsAsync())[^1].EventType);
    }

    [Fact]
    public async Task ASecondSyncAtTheSameTime_IsTurnedAway()
    {
        fx.Supplier.Feed = [Item("V-9")];
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        // a outra sincronização (outra réplica, ou o job) segura o lock
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(2170001)", Ct);

        await Assert.ThrowsAsync<CatalogSyncBusyException>(SyncAsync);
    }

}

/// <summary>O botão do painel (fixture própria: o feed dele não se mistura com o do ciclo de vida).</summary>
public sealed class CatalogSyncApiTests(FulfillmentFixture fx) : IClassFixture<FulfillmentFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SupplierFeedItem Item(string vid, long cents) =>
        new(vid, "CJ" + vid, "Item " + vid, "Mouse", cents, "USD", null, null);

    [Fact]
    public async Task Admin_SyncsOnDemand()
    {
        fx.Supplier.Feed = [Item("V-1", 1500), Item("V-2", 3290)];
        using var client = fx.Factory.CreateClient();
        async Task<HttpResponseMessage> PostAsync(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/supplier-catalog/sync");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await client.SendAsync(request, Ct);
        }

        using var ok = await PostAsync(fx.Identity.AdminToken());
        using var customer = await PostAsync(fx.Identity.CustomerToken());
        fx.Supplier.Feed = null;
        using var down = await PostAsync(fx.Identity.AdminToken());

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        ContractAssert.AssertSchema("fulfillment.v1.yaml", "/components/schemas/CatalogSyncResult", await ok.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
    }
}
