using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Orders.Application.Catalog;
using CompraFacil.Orders.Domain;
using CompraFacil.Orders.Infrastructure;
using CompraFacil.Orders.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Orders.IntegrationTests;

/// <summary>
/// Handlers do snapshot do catálogo contra o Postgres. Cada chamada imita o EventConsumer: um escopo,
/// o handler e o SaveChanges (o transporte Kafka + inbox é testado no BuildingBlocks).
/// </summary>
public sealed class CatalogSnapshotTests(OrdersFixture fx) : IClassFixture<OrdersFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset s_now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private async Task HandleAsync<TData, THandler>(TData data) where THandler : IHandleEvent<TData>
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);
        await handler.HandleAsync(data, new EventContext(Guid.NewGuid(), typeof(TData).Name, "t", "c", s_now), Ct);
        await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().SaveChangesAsync(Ct);
    }

    private async Task<ProductSnapshotRecord?> ProductAsync(Guid id)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().ProductSnapshots.FindAsync([id], Ct);
    }

    private async Task<CampaignSnapshotRecord?> CampaignAsync(Guid id)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().CampaignSnapshots.FindAsync([id], Ct);
    }

    private static ProductSnapshotData Product(Guid id, int version, long cents, string status = "active") =>
        new(id, "SKU-" + version, "Produto v" + version, Guid.NewGuid(), new Money(cents, "BRL"), status, version, s_now.AddMinutes(version));

    [Fact]
    public async Task Product_NewerVersionWins_OlderIsIgnored()
    {
        var id = Guid.NewGuid();

        await HandleAsync<ProductSnapshotData, ProductChanged>(Product(id, version: 1, cents: 1000));
        await HandleAsync<ProductSnapshotData, ProductChanged>(Product(id, version: 3, cents: 900));
        await HandleAsync<ProductSnapshotData, ProductChanged>(Product(id, version: 2, cents: 950)); // atrasado

        var row = await ProductAsync(id);
        Assert.NotNull(row);
        Assert.Equal(3, row.Version);
        Assert.Equal(900, row.PriceCents);
        Assert.Equal("Produto v3", row.Name);
        Assert.False(row.Deleted);
    }

    [Fact]
    public async Task Product_DeleteIsATombstone_ThatBlocksOlderEvents()
    {
        var id = Guid.NewGuid();
        await HandleAsync<ProductSnapshotData, ProductChanged>(Product(id, version: 1, cents: 1000));

        await HandleAsync<ProductDeletedData, ProductDeleted>(new ProductDeletedData(id, "SKU-1", Version: 2));
        await HandleAsync<ProductSnapshotData, ProductChanged>(Product(id, version: 1, cents: 1000)); // redelivery velho

        var row = await ProductAsync(id);
        Assert.True(row!.Deleted);
        Assert.Equal(2, row.Version);
    }

    [Fact]
    public async Task Product_DeleteBeforeCreate_KeepsTheProductDeleted()
    {
        var id = Guid.NewGuid();

        await HandleAsync<ProductDeletedData, ProductDeleted>(new ProductDeletedData(id, "SKU-X", Version: 2));
        await HandleAsync<ProductSnapshotData, ProductChanged>(Product(id, version: 1, cents: 1000));

        Assert.True((await ProductAsync(id))!.Deleted);
    }

    [Fact]
    public async Task Campaign_KeepsTheLatestProductList_AndDeleteClearsIt()
    {
        var id = Guid.NewGuid();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        CampaignSnapshotData Campaign(int version, params Guid[] products) =>
            new(id, "black-friday", "Black Friday", 30, s_now, s_now.AddDays(3), products, version);

        await HandleAsync<CampaignSnapshotData, CampaignChanged>(Campaign(1));
        await HandleAsync<CampaignSnapshotData, CampaignChanged>(Campaign(3, a, b));
        await HandleAsync<CampaignSnapshotData, CampaignChanged>(Campaign(2, a)); // atrasado

        var row = await CampaignAsync(id);
        Assert.Equal(3, row!.Version);
        Assert.Equal(new[] { a, b }.Order(), row.ProductIds.Order());
        Assert.Equal(30, row.DiscountPercent);

        await HandleAsync<CampaignDeletedData, CampaignDeleted>(new CampaignDeletedData(id, Version: 4));
        row = await CampaignAsync(id);
        Assert.True(row!.Deleted);
        Assert.Empty(row.ProductIds);
    }

    [Fact]
    public void CatalogEvents_InTheContractFormat_MapToTheOrdersTypes()
    {
        const string product = """
            {"event_id":"0190a3c4-1111-7000-8000-000000000001","event_type":"ProductUpdated","event_version":1,
             "occurred_at":"2026-11-27T12:00:00Z","producer":"catalog","correlation_id":"req-1","causation_id":null,
             "data":{"product_id":"0f8fad5b-d9cb-469f-a165-70867728950e","sku":"AUD-000123","name":"Fone",
                     "category_id":"6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d","price":{"amount_cents":19990,"currency":"BRL"},
                     "status":"active","version":4,"updated_at":"2026-11-27T12:00:00Z"}}
            """;
        const string campaign = """
            {"event_id":"0190a3c4-1111-7000-8000-000000000002","event_type":"CampaignUpdated","event_version":1,
             "occurred_at":"2026-11-27T12:00:00Z","producer":"catalog","correlation_id":"req-2","causation_id":null,
             "data":{"campaign_id":"1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e5f","slug":"black-friday","name":"Black Friday",
                     "discount_percent":30,"starts_at":"2026-11-27T00:00:00Z","ends_at":"2026-11-30T00:00:00Z",
                     "product_ids":["0f8fad5b-d9cb-469f-a165-70867728950e"],"version":2}}
            """;
        // o exemplo segue o contrato do produtor...
        ContractAssert.AssertEvent("catalog.v1.yaml", "ProductUpdated", product);
        ContractAssert.AssertEvent("catalog.v1.yaml", "CampaignUpdated", campaign);

        // ...e o consumidor lê cada campo
        var p = EventEnvelope.TryParse(product)!.DataAs<ProductSnapshotData>();
        Assert.Equal(new Money(19990, "BRL"), p.Price);
        Assert.Equal(("AUD-000123", "active", 4), (p.Sku, p.Status, p.Version));
        Assert.Equal(Guid.Parse("6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d"), p.CategoryId);

        var c = EventEnvelope.TryParse(campaign)!.DataAs<CampaignSnapshotData>();
        Assert.Equal((30, 2), (c.DiscountPercent, c.Version));
        Assert.Equal(new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero), c.EndsAt);
        Assert.Equal([Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e")], c.ProductIds);
    }
}
