using System.Net.Http.Json;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Orders.Application.Orders;
using CompraFacil.Orders.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Orders.IntegrationTests;

/// <summary>O orders fechando a SAGA: resultado do inventory/payments → estado do pedido + evento.</summary>
public sealed class SagaTests(OrdersFixture fx) : IClassFixture<OrdersFixture>
{
    private readonly HttpClient _http = fx.ClientAs(); // um cliente: o dono dos pedidos do teste

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> PlaceOrderAsync()
    {
        var product = fx.Catalog.Add(new CatalogProduct("SKU-SAGA", "Produto", 2500));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/orders")
        {
            Content = JsonContent.Create(new
            {
                items = new[] { new { product_id = product, quantity = 2 } },
                payment = new { method = "pix" },
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await _http.SendAsync(request, Ct);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private async Task HandleAsync<TData, THandler>(TData data) where THandler : IHandleEvent<TData>
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);
        await handler.HandleAsync(data, new EventContext(Guid.NewGuid(), typeof(TData).Name, "t", "c", DateTimeOffset.UtcNow), Ct);
        await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().SaveChangesAsync(Ct);
    }

    private async Task<JsonElement> GetOrderAsync(Guid id) =>
        await _http.GetFromJsonAsync<JsonElement>($"/v1/orders/{id}", Ct);

    /// <summary>Eventos do pedido além do OrderPlaced, validados no contrato.</summary>
    private async Task<List<OutboxMessage>> OutcomeEventsAsync(Guid id)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var events = await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Set<OutboxMessage>()
            .Where(m => m.MessageKey == id.ToString() && m.EventType != OrderEvents.OrderPlaced).ToListAsync(Ct);
        foreach (var e in events)
        {
            ContractAssert.AssertEvent("orders.v1.yaml", e.EventType, e.Payload);
        }
        return events;
    }

    [Fact]
    public async Task PaymentApproved_ConfirmsTheOrder_AndPublishesOrderConfirmed()
    {
        var order = await PlaceOrderAsync();
        var payment = Guid.NewGuid();

        await HandleAsync<PaymentApprovedData, OnPaymentApproved>(new PaymentApprovedData(order, payment));

        var json = await GetOrderAsync(order);
        Assert.Equal("confirmed", json.GetProperty("status").GetString());
        Assert.Equal(payment, json.GetProperty("payment_id").GetGuid());
        var confirmed = EventEnvelope.TryParse(Assert.Single(await OutcomeEventsAsync(order)).Payload)!.DataAs<OrderConfirmedData>();
        Assert.Equal(("ana@exemplo.com", payment, 5000L), (confirmed.CustomerEmail, confirmed.PaymentId, confirmed.Total.AmountCents));
    }

    [Fact]
    public async Task StockReserved_MarksTheChargingStep_WithoutPublishing()
    {
        var order = await PlaceOrderAsync();
        Assert.Equal(JsonValueKind.Null, (await GetOrderAsync(order)).GetProperty("stock_reserved_at").ValueKind);

        await HandleAsync<StockReservedData, OnStockReserved>(new StockReservedData(order));

        var json = await GetOrderAsync(order);
        Assert.Equal("pending", json.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, json.GetProperty("stock_reserved_at").ValueKind);
        ContractAssert.AssertSchema("orders.v1.yaml", "/components/schemas/Order", json.GetRawText());
        Assert.Empty(await OutcomeEventsAsync(order)); // quem segue a SAGA é o payments
    }

    [Fact]
    public async Task StockReserved_AfterThePaymentResult_IsIgnored()
    {
        var order = await PlaceOrderAsync();
        await HandleAsync<PaymentApprovedData, OnPaymentApproved>(new PaymentApprovedData(order, Guid.NewGuid()));
        var confirmed = await GetOrderAsync(order);

        await HandleAsync<StockReservedData, OnStockReserved>(new StockReservedData(order));

        var json = await GetOrderAsync(order);
        Assert.Equal("confirmed", json.GetProperty("status").GetString());
        Assert.Equal(confirmed.GetProperty("updated_at").GetString(), json.GetProperty("updated_at").GetString());
        Assert.Equal(confirmed.GetProperty("stock_reserved_at").GetString(), json.GetProperty("stock_reserved_at").GetString());
    }

    [Fact]
    public async Task StockReservationFailed_CancelsOutOfStock()
    {
        var order = await PlaceOrderAsync();

        await HandleAsync<StockReservationFailedData, OnStockReservationFailed>(new StockReservationFailedData(order));

        var json = await GetOrderAsync(order);
        Assert.Equal(("cancelled", "out_of_stock"), (json.GetProperty("status").GetString(), json.GetProperty("cancellation_reason").GetString()));
        var cancelled = EventEnvelope.TryParse(Assert.Single(await OutcomeEventsAsync(order)).Payload)!;
        Assert.Equal("out_of_stock", cancelled.Data.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, cancelled.Data.GetProperty("payment_failure_reason").ValueKind);
    }

    [Fact]
    public async Task PaymentFailed_CancelsWithTheReason()
    {
        var order = await PlaceOrderAsync();
        var payment = Guid.NewGuid();

        await HandleAsync<PaymentFailedData, OnPaymentFailed>(new PaymentFailedData(order, payment, "insufficient_funds"));

        var json = await GetOrderAsync(order);
        Assert.Equal(("cancelled", "payment_failed"), (json.GetProperty("status").GetString(), json.GetProperty("cancellation_reason").GetString()));
        Assert.Equal(payment, json.GetProperty("payment_id").GetGuid());
        var cancelled = EventEnvelope.TryParse(Assert.Single(await OutcomeEventsAsync(order)).Payload)!;
        Assert.Equal("insufficient_funds", cancelled.Data.GetProperty("payment_failure_reason").GetString());
    }

    [Fact]
    public async Task FinishedOrder_IgnoresLateAndRepeatedEvents()
    {
        var order = await PlaceOrderAsync();
        await HandleAsync<PaymentApprovedData, OnPaymentApproved>(new PaymentApprovedData(order, Guid.NewGuid()));

        await HandleAsync<PaymentApprovedData, OnPaymentApproved>(new PaymentApprovedData(order, Guid.NewGuid()));
        await HandleAsync<PaymentFailedData, OnPaymentFailed>(new PaymentFailedData(order, Guid.NewGuid(), "card_declined"));
        await HandleAsync<StockReservationFailedData, OnStockReservationFailed>(new StockReservationFailedData(order));

        Assert.Equal("confirmed", (await GetOrderAsync(order)).GetProperty("status").GetString());
        Assert.Single(await OutcomeEventsAsync(order));
    }

    [Fact]
    public async Task UnknownOrder_IsSkipped()
    {
        var unknown = Guid.NewGuid();

        await HandleAsync<PaymentApprovedData, OnPaymentApproved>(new PaymentApprovedData(unknown, Guid.NewGuid()));

        Assert.Empty(await OutcomeEventsAsync(unknown));
    }

    [Fact]
    public async Task ConflictingResultsAtTheSameTime_OnlyOneWins()
    {
        // réplicas recebendo, ao mesmo tempo, resultados que não podem valer juntos
        var orders = new List<Guid>();
        for (var i = 0; i < 8; i++)
        {
            orders.Add(await PlaceOrderAsync());
        }

        await Task.WhenAll(orders.SelectMany(o => new[]
        {
            HandleAsync<PaymentApprovedData, OnPaymentApproved>(new PaymentApprovedData(o, Guid.NewGuid())),
            HandleAsync<PaymentFailedData, OnPaymentFailed>(new PaymentFailedData(o, Guid.NewGuid(), "card_declined")),
        }));

        foreach (var order in orders)
        {
            var events = await OutcomeEventsAsync(order);
            var status = (await GetOrderAsync(order)).GetProperty("status").GetString();
            var single = Assert.Single(events);
            Assert.Equal(status == "confirmed" ? OrderEvents.OrderConfirmed : OrderEvents.OrderCancelled, single.EventType);
        }
    }

    private async Task<Guid> PaidOrderAsync()
    {
        var order = await PlaceOrderAsync();
        await HandleAsync<PaymentApprovedData, OnPaymentApproved>(new PaymentApprovedData(order, Guid.NewGuid()));
        return order;
    }

    private static SupplierOrderShippedData Shipped(Guid order) =>
        new(order, "BR123456789", "Correios", "https://t.17track.net/en#nums=BR123456789");

    private async Task<List<string>> EventTypesAsync(Guid order) =>
        [.. (await OutcomeEventsAsync(order)).OrderBy(e => e.Id).Select(e => e.EventType)];

    [Fact]
    public async Task PaidOrder_FollowsTheSupplier_UntilDelivered()
    {
        var order = await PaidOrderAsync();

        await HandleAsync<SupplierOrderPlacedData, OnSupplierOrderPlaced>(new SupplierOrderPlacedData(order));
        var placed = await GetOrderAsync(order);
        Assert.Equal("confirmed", placed.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, placed.GetProperty("sent_to_supplier_at").ValueKind);
        Assert.Equal([OrderEvents.OrderConfirmed], await EventTypesAsync(order)); // o aceite não publica

        await HandleAsync<SupplierOrderShippedData, OnSupplierOrderShipped>(Shipped(order));
        var shipped = await GetOrderAsync(order);
        Assert.Equal("shipped", shipped.GetProperty("status").GetString());
        Assert.Equal("BR123456789", shipped.GetProperty("tracking").GetProperty("number").GetString());
        ContractAssert.AssertSchema("orders.v1.yaml", "/components/schemas/Order", shipped.GetRawText());

        await HandleAsync<SupplierOrderDeliveredData, OnSupplierOrderDelivered>(new SupplierOrderDeliveredData(order));
        var delivered = await GetOrderAsync(order);
        Assert.Equal("delivered", delivered.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, delivered.GetProperty("delivered_at").ValueKind);
        ContractAssert.AssertSchema("orders.v1.yaml", "/components/schemas/Order", delivered.GetRawText());
        Assert.Equal([OrderEvents.OrderConfirmed, OrderEvents.OrderShipped, OrderEvents.OrderDelivered], await EventTypesAsync(order));
        var shippedEvent = (await OutcomeEventsAsync(order)).Single(e => e.EventType == OrderEvents.OrderShipped);
        var data = EventEnvelope.TryParse(shippedEvent.Payload)!.DataAs<OrderShippedData>();
        Assert.Equal(("ana@exemplo.com", "Correios"), (data.CustomerEmail, data.Tracking.Provider));
    }

    [Fact]
    public async Task SupplierFailure_CancelsThePaidOrder_AndWaitsForTheRefund()
    {
        var order = await PaidOrderAsync();

        await HandleAsync<SupplierOrderFailedData, OnSupplierOrderFailed>(new SupplierOrderFailedData(order, "out_of_stock"));

        var cancelled = await GetOrderAsync(order);
        Assert.Equal(("cancelled", "supplier_failed"), (cancelled.GetProperty("status").GetString(), cancelled.GetProperty("cancellation_reason").GetString()));
        Assert.Equal(JsonValueKind.Null, cancelled.GetProperty("refunded_at").ValueKind);
        Assert.Equal([OrderEvents.OrderConfirmed, OrderEvents.OrderCancelled], await EventTypesAsync(order));
        var e = EventEnvelope.TryParse((await OutcomeEventsAsync(order)).Single(m => m.EventType == OrderEvents.OrderCancelled).Payload)!;
        Assert.Equal("supplier_failed", e.Data.GetProperty("reason").GetString());

        await HandleAsync<PaymentRefundedData, OnPaymentRefunded>(new PaymentRefundedData(order, Guid.NewGuid(), true));
        await HandleAsync<PaymentRefundedData, OnPaymentRefunded>(new PaymentRefundedData(order, Guid.NewGuid(), true)); // repetido

        var refunded = await GetOrderAsync(order);
        Assert.Equal(JsonValueKind.String, refunded.GetProperty("refunded_at").ValueKind);
        ContractAssert.AssertSchema("orders.v1.yaml", "/components/schemas/Order", refunded.GetRawText());
        Assert.Equal(2, (await OutcomeEventsAsync(order)).Count); // o estorno não publica
    }

    [Fact]
    public async Task SupplierEvents_OnlyMoveForward()
    {
        var pending = await PlaceOrderAsync();
        await HandleAsync<SupplierOrderShippedData, OnSupplierOrderShipped>(Shipped(pending));
        Assert.Equal("pending", (await GetOrderAsync(pending)).GetProperty("status").GetString()); // ainda não pago

        var order = await PaidOrderAsync();
        await HandleAsync<SupplierOrderShippedData, OnSupplierOrderShipped>(Shipped(order));
        await HandleAsync<SupplierOrderFailedData, OnSupplierOrderFailed>(new SupplierOrderFailedData(order, "supplier_cancelled")); // depois de enviado
        await HandleAsync<SupplierOrderDeliveredData, OnSupplierOrderDelivered>(new SupplierOrderDeliveredData(order));
        await HandleAsync<SupplierOrderShippedData, OnSupplierOrderShipped>(Shipped(order)); // atrasado
        await HandleAsync<SupplierOrderPlacedData, OnSupplierOrderPlaced>(new SupplierOrderPlacedData(order)); // atrasado

        Assert.Equal("delivered", (await GetOrderAsync(order)).GetProperty("status").GetString());
        Assert.Equal([OrderEvents.OrderConfirmed, OrderEvents.OrderShipped, OrderEvents.OrderDelivered], await EventTypesAsync(order));
    }

    [Fact]
    public async Task PaymentPending_ShowsThePix_OnTheOrder()
    {
        var order = await PlaceOrderAsync();
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);

        await HandleAsync<PaymentPendingData, OnPaymentPending>(
            new PaymentPendingData(order, Guid.NewGuid(), new PixData("00020126SIM", "iVBORw0KGgo=", "http://sim/pix/1", expires)));
        await HandleAsync<PaymentPendingData, OnPaymentPending>( // repetido
            new PaymentPendingData(order, Guid.NewGuid(), new PixData("OUTRO", "x", null, expires)));

        var json = await GetOrderAsync(order);
        ContractAssert.AssertSchema("orders.v1.yaml", "/components/schemas/Order", json.GetRawText());
        Assert.Equal("pending", json.GetProperty("status").GetString());
        var pix = json.GetProperty("payment").GetProperty("pix");
        Assert.Equal(("00020126SIM", "http://sim/pix/1"), (pix.GetProperty("qr_code").GetString(), pix.GetProperty("ticket_url").GetString()));
        Assert.Empty(await OutcomeEventsAsync(order)); // não publica
    }

    [Fact]
    public async Task PartialRefund_DoesNotMarkTheOrderAsRefunded()
    {
        var order = await PaidOrderAsync();
        await HandleAsync<SupplierOrderFailedData, OnSupplierOrderFailed>(new SupplierOrderFailedData(order, "out_of_stock"));

        await HandleAsync<PaymentRefundedData, OnPaymentRefunded>(new PaymentRefundedData(order, Guid.NewGuid(), false));
        Assert.Equal(JsonValueKind.Null, (await GetOrderAsync(order)).GetProperty("refunded_at").ValueKind);

        await HandleAsync<PaymentRefundedData, OnPaymentRefunded>(new PaymentRefundedData(order, Guid.NewGuid(), true));
        Assert.Equal(JsonValueKind.String, (await GetOrderAsync(order)).GetProperty("refunded_at").ValueKind);
    }
}
