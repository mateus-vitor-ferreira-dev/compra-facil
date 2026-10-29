using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Inventory.Application.Stock;
using CompraFacil.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Inventory.IntegrationTests;

/// <summary>Os passos do inventory na SAGA, contra o Postgres, com os eventos validados no contrato.</summary>
public sealed class ReservationSagaTests(InventoryFixture fx) : IClassFixture<InventoryFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task ReserveAsync(Guid orderId, params (Guid Product, int Quantity)[] items) =>
        fx.HandleAsync<OrderPlacedData, ReserveStock>(
            new OrderPlacedData(orderId, [.. items.Select(i => new OrderLine(i.Product, i.Quantity))]));

    private Task<StockItem> StockAsync(Guid productId) =>
        fx.InDbAsync(db => db.Stock.AsNoTracking().SingleAsync(s => s.ProductId == productId, Ct));

    private async Task<OutboxMessage> SingleEventAsync(Guid orderId, string type)
    {
        var events = await fx.EventsForAsync(orderId);
        var e = Assert.Single(events, m => m.EventType == type);
        ContractAssert.AssertEvent("inventory.v1.yaml", type, e.Payload);
        return e;
    }

    [Fact]
    public async Task ConcurrentReservations_NeverOversell()
    {
        // critério da #75: 20 pedidos simultâneos de 1 unidade para um produto com 5
        var product = await fx.ProductWithStockAsync(onHand: 5);
        var orders = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToList();

        await Task.WhenAll(orders.Select(o => ReserveAsync(o, (product, 1))));

        var stock = await StockAsync(product);
        Assert.Equal(5, stock.OnHand);
        Assert.Equal(5, stock.Reserved);
        Assert.Equal(0, stock.Available);

        var types = new List<string>();
        foreach (var order in orders)
        {
            types.Add(Assert.Single(await fx.EventsForAsync(order)).EventType);
        }
        Assert.Equal(5, types.Count(t => t == InventoryEvents.StockReserved));
        Assert.Equal(15, types.Count(t => t == InventoryEvents.StockReservationFailed));
    }

    [Fact]
    public async Task ConcurrentMultiItemOrders_KeepEveryProductConsistent()
    {
        var a = await fx.ProductWithStockAsync(onHand: 6);
        var b = await fx.ProductWithStockAsync(onHand: 4);
        // pedidos com os dois produtos em ordens diferentes: sem deadlock e sem vender a mais
        var orders = Enumerable.Range(0, 12).Select(i => (Id: Guid.NewGuid(), Items: i % 2 == 0
            ? new[] { (a, 1), (b, 1) } : [(b, 1), (a, 1)])).ToList();

        await Task.WhenAll(orders.Select(o => ReserveAsync(o.Id, o.Items)));

        var (sa, sb) = (await StockAsync(a), await StockAsync(b));
        Assert.Equal(4, sb.Reserved); // b limita: 4 pedidos inteiros
        Assert.Equal(4, sa.Reserved); // tudo ou nada: a não reserva nos pedidos recusados
        var reserved = await fx.InDbAsync(db => db.Reservations.CountAsync(r => orders.Select(o => o.Id).Contains(r.OrderId), Ct));
        Assert.Equal(4, reserved);
    }

    [Fact]
    public async Task Reserve_IsAllOrNothing_AndExplainsTheFailure()
    {
        var plenty = await fx.ProductWithStockAsync(onHand: 10);
        var scarce = await fx.ProductWithStockAsync(onHand: 1);
        var unknown = Guid.NewGuid();
        var order = Guid.NewGuid();

        await ReserveAsync(order, (plenty, 3), (scarce, 2), (unknown, 1));

        Assert.Equal(0, (await StockAsync(plenty)).Reserved);
        var failed = EventEnvelope.TryParse((await SingleEventAsync(order, InventoryEvents.StockReservationFailed)).Payload)!
            .DataAs<StockReservationFailedData>();
        Assert.Equal(
            [(scarce, 2, 1, UnavailableReason.InsufficientStock), (unknown, 1, 0, UnavailableReason.UnknownProduct)],
            failed.UnavailableItems.Select(i => (i.ProductId, i.Requested, i.Available, i.Reason)).OrderBy(i => i.Reason));
    }

    [Fact]
    public async Task DiscontinuedProduct_IsNotReserved()
    {
        var product = await fx.ProductWithStockAsync(onHand: 10);
        await fx.HandleAsync<ProductDeletedData, ProductDeleted>(new ProductDeletedData(product));
        var order = Guid.NewGuid();

        await ReserveAsync(order, (product, 1));

        await SingleEventAsync(order, InventoryEvents.StockReservationFailed);
        Assert.Equal(0, (await StockAsync(product)).Reserved);
    }

    [Fact]
    public async Task Reserve_PublishesStockReserved_OncePerOrder()
    {
        var product = await fx.ProductWithStockAsync(onHand: 10);
        var order = Guid.NewGuid();

        await ReserveAsync(order, (product, 3));
        await ReserveAsync(order, (product, 3)); // OrderPlaced repetido com outro event_id

        var reserved = EventEnvelope.TryParse((await SingleEventAsync(order, InventoryEvents.StockReserved)).Payload)!
            .DataAs<StockReservedData>();
        Assert.Equal([new ReservedItem(product, 3)], reserved.Items);
        Assert.Equal(3, (await StockAsync(product)).Reserved);
    }

    [Fact]
    public async Task Confirm_TurnsTheReservationIntoAWithdrawal()
    {
        var product = await fx.ProductWithStockAsync(onHand: 10);
        var order = Guid.NewGuid();
        await ReserveAsync(order, (product, 4));

        await fx.HandleAsync<OrderConfirmedData, CommitReservation>(new OrderConfirmedData(order));
        await fx.HandleAsync<OrderConfirmedData, CommitReservation>(new OrderConfirmedData(order)); // repetido: sem efeito

        var stock = await StockAsync(product);
        Assert.Equal((6, 0), (stock.OnHand, stock.Reserved));
        var reservation = await fx.InDbAsync(db => db.Reservations.AsNoTracking().SingleAsync(r => r.OrderId == order, Ct));
        Assert.Equal(ReservationStatus.Committed, reservation.Status);
        Assert.Single(await fx.EventsForAsync(order)); // só o StockReserved: efetivar não publica
    }

    [Fact]
    public async Task Cancel_ReleasesTheReservation_AndPublishesStockReleased()
    {
        var product = await fx.ProductWithStockAsync(onHand: 10);
        var order = Guid.NewGuid();
        await ReserveAsync(order, (product, 4));

        await fx.HandleAsync<OrderCancelledData, ReleaseReservation>(new OrderCancelledData(order, "payment_failed"));

        var stock = await StockAsync(product);
        Assert.Equal((10, 0), (stock.OnHand, stock.Reserved));
        var released = EventEnvelope.TryParse((await SingleEventAsync(order, InventoryEvents.StockReleased)).Payload)!
            .DataAs<StockReleasedData>();
        Assert.Equal([new ReservedItem(product, 4)], released.Items);
    }

    [Fact]
    public async Task Cancel_WithoutReservation_PublishesNothing()
    {
        var order = Guid.NewGuid(); // recusado por falta de estoque: não há reserva

        await fx.HandleAsync<OrderCancelledData, ReleaseReservation>(new OrderCancelledData(order, "out_of_stock"));

        Assert.Empty(await fx.EventsForAsync(order));
    }

    [Fact]
    public async Task NewCatalogProduct_StartsWithTheConfiguredStock()
    {
        var product = Guid.NewGuid();

        await fx.HandleAsync<ProductCreatedData, ProductCreated>(new ProductCreatedData(product, null));
        await fx.HandleAsync<ProductCreatedData, ProductCreated>(new ProductCreatedData(product, null)); // repetido

        Assert.Equal(7, (await StockAsync(product)).OnHand);
    }
}
