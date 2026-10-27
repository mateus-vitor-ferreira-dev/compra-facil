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
}
