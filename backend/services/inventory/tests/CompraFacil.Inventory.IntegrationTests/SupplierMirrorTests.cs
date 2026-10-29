using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Inventory.Application.Stock;
using CompraFacil.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Inventory.IntegrationTests;

/// <summary>O estoque do produto de dropshipping espelha o do fornecedor e é conferido antes de reservar (#218).</summary>
public sealed class SupplierMirrorTests(InventoryFixture fx) : IClassFixture<InventoryFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset s_t0 = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private Task<StockItem> StockAsync(Guid productId) =>
        fx.InDbAsync(db => db.Stock.AsNoTracking().SingleAsync(s => s.ProductId == productId, Ct));

    private Task CreateAsync(Guid product, string vid) =>
        fx.HandleAsync<ProductCreatedData, ProductCreated>(new ProductCreatedData(product, new SupplierRefData("cj", vid)));

    private Task StockChangedAsync(string vid, int available, DateTimeOffset at) =>
        fx.HandleAsync<SupplierStockChangedData, OnSupplierStockChanged>(new SupplierStockChangedData("cj", vid, available, at));

    private Task ReserveAsync(Guid order, Guid product, int quantity) =>
        fx.HandleAsync<OrderPlacedData, ReserveStock>(new OrderPlacedData(order, [new OrderLine(product, quantity)]));

    private async Task<string> OutcomeAsync(Guid order)
    {
        var e = Assert.Single(await fx.EventsForAsync(order));
        ContractAssert.AssertEvent("inventory.v1.yaml", e.EventType, e.Payload);
        return e.EventType;
    }

    [Fact]
    public async Task Mirror_FollowsTheSupplier_WhicheverEventComesFirst()
    {
        // o produto chega antes da leitura: nasce zerado (não herda o estoque inicial da loja)
        var first = Guid.NewGuid();
        await CreateAsync(first, "V-A");
        Assert.Equal((0, "V-A"), ((await StockAsync(first)).Available, (await StockAsync(first)).SupplierVid));
        await StockChangedAsync("V-A", 5, s_t0);
        Assert.Equal(5, (await StockAsync(first)).Available);
        await StockChangedAsync("V-A", 99, s_t0.AddMinutes(-1)); // atrasado
        Assert.Equal(5, (await StockAsync(first)).Available);

        // a leitura chega antes do produto: ele já nasce espelhado
        await StockChangedAsync("V-B", 8, s_t0);
        var second = Guid.NewGuid();
        await CreateAsync(second, "V-B");
        Assert.Equal(8, (await StockAsync(second)).Available);
    }

    [Fact]
    public async Task ExistingProduct_IsLinked_OnItsFirstUpdate()
    {
        var product = Guid.NewGuid();
        await fx.HandleAsync<ProductCreatedData, ProductCreated>(new ProductCreatedData(product, null)); // de antes do #218
        Assert.Equal(7, (await StockAsync(product)).Available);
        await StockChangedAsync("V-E", 2, s_t0);

        await fx.HandleAsync<ProductCreatedData, ProductUpdated>(new ProductCreatedData(product, new SupplierRefData("cj", "V-E")));

        var item = await StockAsync(product);
        Assert.Equal(("V-E", 2), (item.SupplierVid, item.Available));
    }

    [Fact]
    public async Task Reservation_ChecksTheSupplierLive_AndRefusesBeforeCharging()
    {
        var product = Guid.NewGuid();
        await CreateAsync(product, "V-C");
        await StockChangedAsync("V-C", 10, s_t0); // o espelho diz 10

        // o fornecedor acabou de esgotar: a conferência ao vivo recusa, antes da cobrança
        fx.StockCheck.Stock = new() { ["V-C"] = 0 };
        var refused = Guid.NewGuid();
        await ReserveAsync(refused, product, 1);
        Assert.Equal(InventoryEvents.StockReservationFailed, await OutcomeAsync(refused));
        Assert.Equal(0, (await StockAsync(product)).Available);

        // voltou com 3: reserva, e o disponível desconta a reserva
        fx.StockCheck.Stock = new() { ["V-C"] = 3 };
        var ok = Guid.NewGuid();
        await ReserveAsync(ok, product, 2);
        Assert.Equal(InventoryEvents.StockReserved, await OutcomeAsync(ok));
        var after = await StockAsync(product);
        Assert.Equal((2, 1), (after.Reserved, after.Available));

        // a conferência fora do ar: segue pelo espelho (1 disponível)
        fx.StockCheck.Stock = null;
        var byMirror = Guid.NewGuid();
        await ReserveAsync(byMirror, product, 1);
        Assert.Equal(InventoryEvents.StockReserved, await OutcomeAsync(byMirror));
        var tooMany = Guid.NewGuid();
        await ReserveAsync(tooMany, product, 1);
        Assert.Equal(InventoryEvents.StockReservationFailed, await OutcomeAsync(tooMany));
    }

    [Fact]
    public async Task OwnStock_IsNotChecked_AndMirroredStock_IsNotAdjustedByHand()
    {
        var own = await fx.ProductWithStockAsync(onHand: 4);
        var calls = fx.StockCheck.Calls;
        var order = Guid.NewGuid();
        await ReserveAsync(order, own, 1);
        Assert.Equal(InventoryEvents.StockReserved, await OutcomeAsync(order));
        Assert.Equal(calls, fx.StockCheck.Calls); // estoque próprio: sem conferência no fornecedor

        var mirrored = Guid.NewGuid();
        await CreateAsync(mirrored, "V-D");
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var adjust = scope.ServiceProvider.GetRequiredService<AdjustStock>();
        var e = await Assert.ThrowsAnyAsync<Exception>(() => adjust.HandleAsync(
            new AdjustStockCommand(mirrored, 5, "restock", null, Guid.NewGuid().ToString(), "admin"), Ct));
        Assert.Contains("fornecedor", e.Message, StringComparison.Ordinal);
    }
}
