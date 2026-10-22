using System.Net;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Payments.Application.Payments;
using CompraFacil.Payments.Domain;
using CompraFacil.Payments.Infrastructure;
using CompraFacil.Payments.Infrastructure.Gateway;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Payments.IntegrationTests;

public sealed class PaymentsFixture : IAsyncLifetime
{
    public static readonly Guid DeclinedCustomer = Guid.Parse("00000000-0000-4000-8000-00000000dec1");
    public static readonly Guid FraudCustomer = Guid.Parse("00000000-0000-4000-8000-0000000f4a0d");

    private readonly PostgresFixture _postgres = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _postgres.ConnectionString)
            .UseSetting("Kafka:Enabled", "false")
            .UseSetting("Payments:Fake:MaxApprovedAmountCents", "100000")
            .UseSetting("Payments:Fake:Latency", "00:00:00.010")
            .UseSetting("Payments:Fake:DeclinedCustomers:0", DeclinedCustomer.ToString())
            .UseSetting("Payments:Fake:FraudCustomers:0", FraudCustomer.ToString()));
        using var _ = Factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

/// <summary>A cobrança na SAGA: junta OrderPlaced e StockReserved, em qualquer ordem, e cobra uma vez.</summary>
public sealed class PaymentsTests(PaymentsFixture fx) : IClassFixture<PaymentsFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task HandleAsync<TData, THandler>(TData data) where THandler : IHandleEvent<TData>
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);
        await handler.HandleAsync(data, new EventContext(Guid.NewGuid(), typeof(TData).Name, "t", "req-pay", DateTimeOffset.UtcNow), Ct);
        await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().SaveChangesAsync(Ct);
    }

    private Task OrderPlacedAsync(Guid order, long cents, Guid? customer = null) =>
        HandleAsync<OrderPlacedData, OnOrderPlaced>(new OrderPlacedData(order, customer ?? Guid.NewGuid(), new Money(cents, "BRL")));

    private Task StockReservedAsync(Guid order) => HandleAsync<StockReservedData, OnStockReserved>(new StockReservedData(order));

    private async Task<List<Payment>> PaymentsOfAsync(Guid order)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Payments.AsNoTracking()
            .Where(p => p.OrderId == order).ToListAsync(Ct);
    }

    private async Task<List<OutboxMessage>> EventsOfAsync(Guid order)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Set<OutboxMessage>()
            .Where(m => m.MessageKey == order.ToString()).ToListAsync(Ct);
    }

    private async Task<T> SingleEventAsync<T>(Guid order, string type)
    {
        var e = Assert.Single(await EventsOfAsync(order));
        Assert.Equal(type, e.EventType);
        ContractAssert.AssertEvent("payments.v1.yaml", type, e.Payload);
        return EventEnvelope.TryParse(e.Payload)!.DataAs<T>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChargesWhenBothEventsArrive_InAnyOrder(bool orderFirst)
    {
        var order = Guid.NewGuid();

        if (orderFirst)
        {
            await OrderPlacedAsync(order, 19990);
            Assert.Empty(await PaymentsOfAsync(order)); // falta o estoque
            await StockReservedAsync(order);
        }
        else
        {
            await StockReservedAsync(order);
            Assert.Empty(await PaymentsOfAsync(order)); // falta o valor
            await OrderPlacedAsync(order, 19990);
        }

        var payment = Assert.Single(await PaymentsOfAsync(order));
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.StartsWith("fake_ch_", payment.ProviderReference, StringComparison.Ordinal);
        var approved = await SingleEventAsync<PaymentApprovedData>(order, PaymentEvents.PaymentApproved);
        Assert.Equal((payment.Id, new Money(19990, "BRL"), "fake"), (approved.PaymentId, approved.Amount, approved.Provider));
    }

    public static TheoryData<long, Guid?, FailureReason> Declines => new()
    {
        { 100_001, null, FailureReason.InsufficientFunds },
        { 5_000, PaymentsFixture.DeclinedCustomer, FailureReason.CardDeclined },
        { 5_000, PaymentsFixture.FraudCustomer, FailureReason.FraudSuspected },
    };

    [Theory]
    [MemberData(nameof(Declines))]
    public async Task DeclinedCharge_PublishesPaymentFailed(long cents, Guid? customer, FailureReason reason)
    {
        var order = Guid.NewGuid();

        await OrderPlacedAsync(order, cents, customer);
        await StockReservedAsync(order);

        var payment = Assert.Single(await PaymentsOfAsync(order));
        Assert.Equal((PaymentStatus.Failed, reason), (payment.Status, payment.FailureReason));
        var failed = await SingleEventAsync<PaymentFailedData>(order, PaymentEvents.PaymentFailed);
        Assert.Equal(reason, failed.FailureReason);
    }

    [Fact]
    public async Task CancelledBeforeStock_IsNeverCharged()
    {
        var order = Guid.NewGuid();

        await OrderPlacedAsync(order, 1000);
        await HandleAsync<OrderCancelledData, OnOrderCancelled>(new OrderCancelledData(order));
        await StockReservedAsync(order);

        Assert.Empty(await PaymentsOfAsync(order));
        Assert.Empty(await EventsOfAsync(order));
    }

    [Fact]
    public async Task RepeatedEvents_ChargeOnce()
    {
        var order = Guid.NewGuid();
        var gateway = fx.Factory.Services.GetRequiredService<FakePaymentGateway>();

        await OrderPlacedAsync(order, 1000);
        await StockReservedAsync(order);
        var charges = gateway.ChargesMade;
        await StockReservedAsync(order);
        await OrderPlacedAsync(order, 1000);

        Assert.Single(await PaymentsOfAsync(order));
        Assert.Single(await EventsOfAsync(order));
        Assert.Equal(charges, gateway.ChargesMade);
    }

    [Fact]
    public async Task SimultaneousEventsOfTheSameOrder_ChargeOnce()
    {
        // réplicas diferentes recebendo OrderPlaced e StockReserved do mesmo pedido ao mesmo tempo
        var orders = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();

        await Task.WhenAll(orders.SelectMany(o => new[] { OrderPlacedAsync(o, 1000), StockReservedAsync(o), StockReservedAsync(o) }));

        foreach (var order in orders)
        {
            Assert.Single(await PaymentsOfAsync(order));
            Assert.Single(await EventsOfAsync(order));
        }
    }

    [Fact]
    public async Task Gateway_IsIdempotentByKey()
    {
        var gateway = fx.Factory.Services.GetRequiredService<IPaymentGateway>();
        var request = new ChargeRequest("chave-" + Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new Money(100, "BRL"));

        var first = await gateway.ChargeAsync(request, Ct);
        var again = await gateway.ChargeAsync(request with { Amount = new Money(999, "BRL") }, Ct); // retry após queda

        Assert.Equal(first, again);
    }

    [Fact]
    public async Task Api_ListsAndGetsPayments_FollowingTheContract()
    {
        var order = Guid.NewGuid();
        await OrderPlacedAsync(order, 4990);
        await StockReservedAsync(order);
        using var http = fx.Factory.CreateClient();

        using var list = await http.GetAsync(new Uri($"/v1/payments?order_id={order}", UriKind.Relative), Ct);
        var listJson = await list.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("payments.v1.yaml", "/paths/~1v1~1payments/get/responses/200/content/application~1json/schema", listJson);
        var id = JsonDocument.Parse(listJson).RootElement.GetProperty("data")[0].GetProperty("id").GetGuid();

        using var one = await http.GetAsync(new Uri($"/v1/payments/{id}", UriKind.Relative), Ct);
        var oneJson = await one.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("payments.v1.yaml", "/components/schemas/Payment", oneJson);
        Assert.Equal("approved", JsonDocument.Parse(oneJson).RootElement.GetProperty("status").GetString());

        using var missing = await http.GetAsync(new Uri($"/v1/payments/{Guid.NewGuid()}", UriKind.Relative), Ct);
        using var noOrder = await http.GetAsync(new Uri("/v1/payments", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noOrder.StatusCode);
    }
}
