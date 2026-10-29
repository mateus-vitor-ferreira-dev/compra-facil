using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Payments.Application.Payments;
using CompraFacil.Payments.Domain;
using CompraFacil.Payments.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompraFacil.Payments.IntegrationTests;

/// <summary>
/// O provedor dos testes: guarda as cobranças em memória, idempotente pela chave como o de verdade. O
/// cartão responde pelo token ("APRO", "FUND", "FRAU", "CONT", "USADO"); o PIX fica pendente até o teste
/// mudar o status (o cliente pagando, ou a validade passando).
/// </summary>
public sealed class FakeProvider : IPaymentProvider
{
    private readonly ConcurrentDictionary<string, ProviderPayment> _byKey = new();
    private readonly ConcurrentDictionary<string, ProviderPayment> _byRef = new();
    private readonly ConcurrentDictionary<string, ProviderRefund> _refunds = new();
    private long _seq = 1000;

    public string Name => "mercadopago";
    public int Created => _byKey.Count;
    public int RefundsMade => _refunds.Count;
    public List<string> Captured { get; } = [];
    public List<string> Cancelled { get; } = [];

    /// <summary>O provedor fora do ar.</summary>
    public bool Down { get; set; }

    public Task<ProviderPayment> CreatePixAsync(PixRequest request, CancellationToken ct) =>
        Create(request.IdempotencyKey, r => new(r, ProviderStatus.Pending,
            Pix: new PixCharge("00020126...SIM" + r, "iVBORw0KGgo=", "http://sim/pix/" + r, request.ExpiresAt)));

    public Task<ProviderPayment> AuthorizeCardAsync(CardRequest request, CancellationToken ct)
    {
        if (request.CardToken == "USADO")
        {
            throw new PaymentProviderRejectedException("token já usado");
        }
        return Create(request.IdempotencyKey, r => request.CardToken switch
        {
            "FUND" => new(r, ProviderStatus.Rejected, FailureReason.InsufficientFunds),
            "FRAU" => new(r, ProviderStatus.Rejected, FailureReason.FraudSuspected),
            "CONT" => new(r, ProviderStatus.InProcess),
            _ => new(r, ProviderStatus.Authorized, CardLastFour: "5682"),
        });
    }

    public Task<ProviderPayment> GetAsync(string reference, CancellationToken ct) => Task.FromResult(_byRef[reference]);

    public Task<ProviderPayment> CaptureAsync(string reference, CancellationToken ct)
    {
        Captured.Add(reference);
        return Task.FromResult(_byRef[reference] = _byRef[reference] with { Status = ProviderStatus.Approved });
    }

    public Task<ProviderPayment> CancelAsync(string reference, CancellationToken ct)
    {
        Cancelled.Add(reference);
        return Task.FromResult(_byRef[reference] = _byRef[reference] with { Status = ProviderStatus.Cancelled });
    }

    public Task<ProviderRefund> RefundAsync(string reference, long? amountCents, string idempotencyKey, CancellationToken ct) =>
        Task.FromResult(_refunds.GetOrAdd(idempotencyKey, _ => new ProviderRefund("re-" + Interlocked.Increment(ref _seq), amountCents ?? 0)));

    public bool VerifyNotification(string dataId, string? requestId, string? signature) => signature == "assinado";

    /// <summary>O que acontece no provedor sem a loja pedir (o cliente paga o PIX, a validade passa).</summary>
    public void Set(string reference, ProviderStatus status) => _byRef[reference] = _byRef[reference] with { Status = status };

    private Task<ProviderPayment> Create(string key, Func<string, ProviderPayment> make)
    {
        if (Down)
        {
            throw new PaymentProviderUnavailableException("fora do ar");
        }
        var payment = _byKey.GetOrAdd(key, _ => make(Interlocked.Increment(ref _seq).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        _byRef.TryAdd(payment.Reference, payment);
        return Task.FromResult(payment);
    }
}

public sealed class PaymentsFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    public TestIdentity Identity { get; } = new();
    public FakeProvider Provider { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _postgres.ConnectionString)
            .UseSetting("Kafka:Enabled", "false")
            .UseSetting("Auth:Jwks", Identity.Jwks)
            .ConfigureTestServices(s =>
            {
                s.RemoveAll<IPaymentProvider>();
                s.AddSingleton<IPaymentProvider>(Provider);
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

/// <summary>A cobrança na SAGA (#220): PIX e cartão, captura quando o fornecedor aceita, e o dinheiro de volta.</summary>
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

    private static OrderPaymentData Card(string token, int installments = 1) => new(PaymentMethod.Card, installments, token, "visa");

    private Task OrderPlacedAsync(Guid order, long cents, OrderPaymentData? payment = null) =>
        HandleAsync<OrderPlacedData, OnOrderPlaced>(new OrderPlacedData(order, Guid.NewGuid(), "ana@exemplo.com", new Money(cents, "BRL"), payment));

    private Task StockReservedAsync(Guid order) => HandleAsync<StockReservedData, OnStockReserved>(new StockReservedData(order));

    private async Task PlacedAndReservedAsync(Guid order, long cents, OrderPaymentData? payment)
    {
        await OrderPlacedAsync(order, cents, payment);
        await StockReservedAsync(order);
    }

    private async Task<Payment> PaymentOfAsync(Guid order)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Payments.AsNoTracking().SingleAsync(p => p.OrderId == order, Ct);
    }

    private async Task<Charge> ChargeOfAsync(Guid order)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Charges.AsNoTracking().SingleAsync(c => c.OrderId == order, Ct);
    }

    /// <summary>Os eventos do pedido, na ordem, validados no contrato.</summary>
    private async Task<List<OutboxMessage>> EventsOfAsync(Guid order)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var events = await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Set<OutboxMessage>()
            .Where(m => m.MessageKey == order.ToString()).OrderBy(m => m.Id).ToListAsync(Ct);
        foreach (var e in events)
        {
            ContractAssert.AssertEvent("payments.v1.yaml", e.EventType, e.Payload);
        }
        return events;
    }

    private async Task<List<string>> TypesOfAsync(Guid order) => [.. (await EventsOfAsync(order)).Select(e => e.EventType)];

    private async Task<T> LastAsync<T>(Guid order, string type) =>
        EventEnvelope.TryParse((await EventsOfAsync(order)).Last(e => e.EventType == type).Payload)!.DataAs<T>();

    private async Task<NotificationResult> NotifyAsync(string reference)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ProviderNotifications>().ApplyAsync(reference, Ct);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Card_IsAuthorized_WhenBothEventsArrive_InAnyOrder(bool orderFirst)
    {
        var order = Guid.NewGuid();
        if (orderFirst)
        {
            await OrderPlacedAsync(order, 19990, Card("APRO", installments: 3));
            await StockReservedAsync(order);
        }
        else
        {
            await StockReservedAsync(order);
            await OrderPlacedAsync(order, 19990, Card("APRO", installments: 3));
        }

        var payment = await PaymentOfAsync(order);
        Assert.Equal((PaymentStatus.Authorized, PaymentMethod.Card, 3, "5682"), (payment.Status, payment.Method, payment.Installments, payment.CardLastFour));
        Assert.Equal(1980, payment.FeeCents); // 9,9% em 3x, arredondado para cima
        Assert.Null((await ChargeOfAsync(order)).CardToken); // o token de uso único não fica guardado
        var approved = await LastAsync<PaymentApprovedData>(order, PaymentEvents.PaymentApproved);
        Assert.False(approved.Captured);
    }

    [Fact]
    public async Task Card_IsCaptured_WhenTheSupplierAccepts()
    {
        var order = Guid.NewGuid();
        await PlacedAndReservedAsync(order, 5000, Card("APRO"));

        await HandleAsync<SupplierOrderPlacedData, OnSupplierOrderPlaced>(new SupplierOrderPlacedData(order));
        await HandleAsync<SupplierOrderPlacedData, OnSupplierOrderPlaced>(new SupplierOrderPlacedData(order)); // repetido

        var payment = await PaymentOfAsync(order);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.NotNull(payment.CapturedAt);
        Assert.Single(fx.Provider.Captured, payment.ProviderReference);
        Assert.Equal([PaymentEvents.PaymentApproved], await TypesOfAsync(order)); // a captura não confirma de novo
    }

    public static TheoryData<string, FailureReason> Declines => new()
    {
        { "FUND", FailureReason.InsufficientFunds },
        { "FRAU", FailureReason.FraudSuspected },
        { "USADO", FailureReason.CardDeclined }, // o provedor recusa o token (4xx)
    };

    [Theory]
    [MemberData(nameof(Declines))]
    public async Task DeclinedCard_PublishesPaymentFailed(string token, FailureReason reason)
    {
        var order = Guid.NewGuid();
        await PlacedAndReservedAsync(order, 5000, Card(token));

        Assert.Equal((PaymentStatus.Failed, reason), ((await PaymentOfAsync(order)).Status, (await PaymentOfAsync(order)).FailureReason));
        Assert.Equal(reason, (await LastAsync<PaymentFailedData>(order, PaymentEvents.PaymentFailed)).FailureReason);
    }

    [Fact]
    public async Task CardInReview_IsDecidedByTheNotification()
    {
        var order = Guid.NewGuid();
        await PlacedAndReservedAsync(order, 5000, Card("CONT"));
        var payment = await PaymentOfAsync(order);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Empty(await EventsOfAsync(order));

        fx.Provider.Set(payment.ProviderReference!, ProviderStatus.Authorized);
        Assert.Equal(NotificationResult.Applied, await NotifyAsync(payment.ProviderReference!));
        Assert.Equal(NotificationResult.Ignored, await NotifyAsync(payment.ProviderReference!)); // repetida

        Assert.Equal(PaymentStatus.Authorized, (await PaymentOfAsync(order)).Status);
    }

    [Fact]
    public async Task Pix_WaitsForTheCustomer_ThenIsPaidOrExpires()
    {
        var paid = Guid.NewGuid();
        await PlacedAndReservedAsync(paid, 12090, payment: null); // sem o meio: PIX
        var payment = await PaymentOfAsync(paid);
        Assert.Equal((PaymentStatus.Pending, PaymentMethod.Pix), (payment.Status, payment.Method));
        var pending = await LastAsync<PaymentPendingData>(paid, PaymentEvents.PaymentPending);
        Assert.StartsWith("00020126", pending.Pix.QrCode, StringComparison.Ordinal);
        Assert.True(pending.Pix.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(25));

        fx.Provider.Set(payment.ProviderReference!, ProviderStatus.Approved); // o cliente pagou no banco
        await NotifyAsync(payment.ProviderReference!);
        payment = await PaymentOfAsync(paid);
        Assert.Equal((PaymentStatus.Approved, 120L), (payment.Status, payment.FeeCents)); // 0,99%
        Assert.True((await LastAsync<PaymentApprovedData>(paid, PaymentEvents.PaymentApproved)).Captured);

        var expired = Guid.NewGuid();
        await PlacedAndReservedAsync(expired, 5000, new OrderPaymentData(PaymentMethod.Pix, null, null, null));
        var unpaid = await PaymentOfAsync(expired);
        fx.Provider.Set(unpaid.ProviderReference!, ProviderStatus.Expired);
        await NotifyAsync(unpaid.ProviderReference!);
        Assert.Equal(FailureReason.PixExpired, (await LastAsync<PaymentFailedData>(expired, PaymentEvents.PaymentFailed)).FailureReason);
        Assert.Equal(NotificationResult.UnknownPayment, await NotifyAsync("nao-existe"));
    }

    [Fact]
    public async Task CancelledAfterPayment_VoidsTheCard_OrRefundsThePix()
    {
        // cartão autorizado e o fornecedor recusou: a autorização é cancelada, nada é cobrado
        var card = Guid.NewGuid();
        await PlacedAndReservedAsync(card, 7990, Card("APRO"));
        await HandleAsync<OrderCancelledData, OnOrderCancelled>(new OrderCancelledData(card));
        await HandleAsync<OrderCancelledData, OnOrderCancelled>(new OrderCancelledData(card)); // repetido
        Assert.Equal(PaymentStatus.Voided, (await PaymentOfAsync(card)).Status);
        var voided = await LastAsync<PaymentRefundedData>(card, PaymentEvents.PaymentRefunded);
        Assert.Equal((RefundKind.Void, true, 7990L), (voided.Kind, voided.Full, voided.Amount.AmountCents));
        Assert.Single(await EventsOfAsync(card), e => e.EventType == PaymentEvents.PaymentRefunded);

        // PIX pago: estorno do valor todo
        var pix = Guid.NewGuid();
        await PlacedAndReservedAsync(pix, 4000, payment: null);
        var payment = await PaymentOfAsync(pix);
        fx.Provider.Set(payment.ProviderReference!, ProviderStatus.Approved);
        await NotifyAsync(payment.ProviderReference!);
        await HandleAsync<OrderCancelledData, OnOrderCancelled>(new OrderCancelledData(pix));
        payment = await PaymentOfAsync(pix);
        Assert.Equal((PaymentStatus.Refunded, 4000L), (payment.Status, payment.RefundedCents));
        Assert.Equal(RefundKind.Refund, (await LastAsync<PaymentRefundedData>(pix, PaymentEvents.PaymentRefunded)).Kind);

        // cancelado antes do estoque: nunca é cobrado
        var early = Guid.NewGuid();
        await OrderPlacedAsync(early, 1000, Card("APRO"));
        await HandleAsync<OrderCancelledData, OnOrderCancelled>(new OrderCancelledData(early));
        await StockReservedAsync(early);
        Assert.Empty(await EventsOfAsync(early));
    }

    [Fact]
    public async Task ProviderDown_ChargesOnTheRetry_WithTheSameKey()
    {
        var order = Guid.NewGuid();
        await OrderPlacedAsync(order, 1000, Card("APRO"));
        fx.Provider.Down = true;
        await Assert.ThrowsAsync<PaymentProviderUnavailableException>(() => StockReservedAsync(order));
        fx.Provider.Down = false;
        var created = fx.Provider.Created;

        await StockReservedAsync(order); // a mensagem volta

        Assert.Equal(PaymentStatus.Authorized, (await PaymentOfAsync(order)).Status);
        Assert.Equal(created + 1, fx.Provider.Created);
    }

    [Fact]
    public async Task SimultaneousEventsOfTheSameOrder_ChargeOnce()
    {
        var orders = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();

        await Task.WhenAll(orders.SelectMany(o => new[] { OrderPlacedAsync(o, 1000, Card("APRO")), StockReservedAsync(o), StockReservedAsync(o) }));

        foreach (var order in orders)
        {
            Assert.Equal([PaymentEvents.PaymentApproved], await TypesOfAsync(order));
        }
    }

    private async Task<HttpResponseMessage> RefundAsync(Guid paymentId, string key, long? amountCents, string? token = null)
    {
        using var http = fx.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/admin/payments/{paymentId}/refunds")
        {
            Content = JsonContent.Create(new { amount_cents = amountCents }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? fx.Identity.AdminToken());
        request.Headers.Add("Idempotency-Key", key);
        return await http.SendAsync(request, Ct);
    }

    [Fact]
    public async Task Admin_RefundsPartOfAPayment_Idempotently()
    {
        var order = Guid.NewGuid();
        await PlacedAndReservedAsync(order, 10000, Card("APRO"));
        await HandleAsync<SupplierOrderPlacedData, OnSupplierOrderPlaced>(new SupplierOrderPlacedData(order));
        var id = (await PaymentOfAsync(order)).Id;
        var before = fx.Provider.RefundsMade;

        using var first = await RefundAsync(id, "devolucao-1", 3000);
        using var again = await RefundAsync(id, "devolucao-1", 3000);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        ContractAssert.AssertSchema("payments.v1.yaml", "/components/schemas/Payment", await first.Content.ReadAsStringAsync(Ct));
        Assert.Equal(before + 1, fx.Provider.RefundsMade);
        Assert.Equal(3000, (await PaymentOfAsync(order)).RefundedCents);

        using var tooMuch = await RefundAsync(id, "devolucao-2", 9000);
        using var customer = await RefundAsync(id, "devolucao-3", 100, fx.Identity.CustomerToken());
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
        var partial = await LastAsync<PaymentRefundedData>(order, PaymentEvents.PaymentRefunded);
        Assert.Equal((3000L, false), (partial.TotalRefundedCents, partial.Full));
    }

    [Fact]
    public async Task Webhook_ChecksTheSignature_AndAppliesThePayment()
    {
        var order = Guid.NewGuid();
        await PlacedAndReservedAsync(order, 5000, payment: null);
        var reference = (await PaymentOfAsync(order)).ProviderReference!;
        fx.Provider.Set(reference, ProviderStatus.Approved);
        using var http = fx.Factory.CreateClient();
        async Task<HttpResponseMessage> PostAsync(string signature)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/webhooks/payments?data.id={reference}&type=payment")
            {
                Content = JsonContent.Create(new { type = "payment", action = "payment.updated", data = new { id = reference } }),
            };
            request.Headers.Add("x-signature", signature);
            request.Headers.Add("x-request-id", Guid.NewGuid().ToString());
            return await http.SendAsync(request, Ct);
        }

        using var forged = await PostAsync("falso");
        using var signed = await PostAsync("assinado");

        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        Assert.Equal("applied", JsonDocument.Parse(await signed.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("result").GetString());
        Assert.Equal(PaymentStatus.Approved, (await PaymentOfAsync(order)).Status);
    }

    [Fact]
    public async Task Api_ListsAndGetsPayments_FollowingTheContract()
    {
        var order = Guid.NewGuid();
        await PlacedAndReservedAsync(order, 4990, payment: null);
        using var http = fx.Factory.CreateClient();

        using var list = await http.GetAsync(new Uri($"/v1/payments?order_id={order}", UriKind.Relative), Ct);
        var listJson = await list.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("payments.v1.yaml", "/paths/~1v1~1payments/get/responses/200/content/application~1json/schema", listJson);
        var id = JsonDocument.Parse(listJson).RootElement.GetProperty("data")[0].GetProperty("id").GetGuid();

        using var one = await http.GetAsync(new Uri($"/v1/payments/{id}", UriKind.Relative), Ct);
        var oneJson = await one.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("payments.v1.yaml", "/components/schemas/Payment", oneJson);
        var root = JsonDocument.Parse(oneJson).RootElement;
        Assert.Equal(("pending", "pix"), (root.GetProperty("status").GetString(), root.GetProperty("method").GetString()));
        Assert.StartsWith("00020126", root.GetProperty("pix").GetProperty("qr_code").GetString(), StringComparison.Ordinal);

        using var missing = await http.GetAsync(new Uri($"/v1/payments/{Guid.NewGuid()}", UriKind.Relative), Ct);
        using var noOrder = await http.GetAsync(new Uri("/v1/payments", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noOrder.StatusCode);
    }
}
