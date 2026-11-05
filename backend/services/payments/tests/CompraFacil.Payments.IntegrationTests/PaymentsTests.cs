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
/// O provedor dos testes, com as regras da Efí: o cartão em dois passos (abrir e pagar; pagar de novo não cobra),
/// aprovado que só se estorna depois de liquidado, e o PIX pendente até o teste mudar o status. O cartão responde
/// pelo token ("APRO", "FUND", "FRAU", "CONT", "USADO", "SEMDADOS").
/// </summary>
public sealed class FakeProvider : IPaymentProvider
{
    private readonly ConcurrentDictionary<string, ProviderPayment> _pixByKey = new();
    private readonly ConcurrentDictionary<string, ProviderPayment> _byRef = new();
    private readonly ConcurrentDictionary<string, ProviderRefund> _refunds = new();
    private long _seq = 1000;

    public string Name => "efi";
    private int _opened;

    public int Created => _pixByKey.Count + _opened;
    public int RefundsMade => _refunds.Count;
    public List<string> Cancelled { get; } = [];

    /// <summary>Cobranças de cartão de fato pagas (o dinheiro saiu), por referência.</summary>
    public ConcurrentDictionary<string, int> Charged { get; } = new();

    /// <summary>Token de notificação → as cobranças que ele diz terem mudado.</summary>
    public ConcurrentDictionary<string, string[]> Notifications { get; } = new();

    /// <summary>Quem pagou cada pedido, como chegou ao provedor.</summary>
    public ConcurrentDictionary<Guid, Payer?> Payers { get; } = new();

    /// <summary>O provedor fora do ar.</summary>
    public bool Down { get; set; }

    /// <summary>A próxima chamada de pagar cobra e cai antes de responder (o payments não sabe se cobrou).</summary>
    public bool FailAfterPayingOnce { get; set; }

    /// <summary>PIX deste valor já nasce vencido (para a conferência do PIX vencido).</summary>
    public const long ExpiredPixCents = 777;

    public Task<ProviderPayment> CreatePixAsync(PixRequest request, CancellationToken ct)
    {
        ThrowIfDown();
        Payers[request.OrderId] = request.Payer;
        var expires = request.Amount.AmountCents == ExpiredPixCents ? DateTimeOffset.UtcNow.AddMinutes(-5) : request.ExpiresAt;
        var payment = _pixByKey.GetOrAdd(request.IdempotencyKey, _ =>
        {
            var r = "cf" + Interlocked.Increment(ref _seq).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new(r, ProviderStatus.Pending, Pix: new PixCharge("00020126...SIM" + r, "iVBORw0KGgo=", "http://sim/pix/" + r, expires));
        });
        _byRef.TryAdd(payment.Reference, payment);
        return Task.FromResult(payment);
    }

    public Task<string> OpenCardChargeAsync(CardRequest request, CancellationToken ct)
    {
        ThrowIfDown();
        Payers[request.OrderId] = request.Payer;
        if (request.CardToken == "SEMDADOS")
        {
            throw new PaymentProviderRejectedException("o cartão exige o telefone");
        }
        Interlocked.Increment(ref _opened);
        var reference = Interlocked.Increment(ref _seq).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _byRef[reference] = new ProviderPayment(reference, ProviderStatus.InProcess);
        return Task.FromResult(reference);
    }

    public Task<ProviderPayment> PayCardAsync(string reference, CardRequest request, CancellationToken ct)
    {
        ThrowIfDown();
        if (request.CardToken == "USADO")
        {
            throw new PaymentProviderRejectedException("token já usado");
        }
        if (!Charged.ContainsKey(reference))
        {
            // a primeira vez decide; pagar de novo a mesma cobrança só a lê (o 3500030 da Efí)
            Charged[reference] = 1;
            _byRef[reference] = request.CardToken switch
            {
                "FUND" => new(reference, ProviderStatus.Rejected, FailureReason.InsufficientFunds),
                "FRAU" => new(reference, ProviderStatus.Rejected, FailureReason.FraudSuspected),
                "CONT" => new(reference, ProviderStatus.InProcess),
                _ => new(reference, ProviderStatus.Authorized, CardLastFour: "5682"),
            };
        }
        if (FailAfterPayingOnce)
        {
            FailAfterPayingOnce = false;
            throw new PaymentProviderUnavailableException("caiu depois de cobrar");
        }
        return Task.FromResult(_byRef[reference]);
    }

    public Task<ProviderPayment> GetAsync(string reference, CancellationToken ct) => Task.FromResult(_byRef[reference]);

    public Task<ProviderPayment> CancelAsync(string reference, CancellationToken ct)
    {
        if (_byRef[reference].Status is not (ProviderStatus.Pending or ProviderStatus.InProcess or ProviderStatus.Expired))
        {
            throw new PaymentProviderRejectedException("só a cobrança que não pagou se cancela");
        }
        Cancelled.Add(reference);
        return Task.FromResult(_byRef[reference] = _byRef[reference] with { Status = ProviderStatus.Cancelled });
    }

    public Task<ProviderRefund> RefundAsync(string reference, long? amountCents, string idempotencyKey, CancellationToken ct)
    {
        if (_byRef[reference].Status != ProviderStatus.Approved)
        {
            throw new PaymentProviderRejectedException("só o pago (liquidado) se estorna");
        }
        return Task.FromResult(_refunds.GetOrAdd(idempotencyKey, _ => new ProviderRefund("re-" + Interlocked.Increment(ref _seq), amountCents ?? 0)));
    }

    public Task<IReadOnlyList<string>> ChangedByNotificationAsync(string token, CancellationToken ct) =>
        Notifications.TryGetValue(token, out var refs)
            ? Task.FromResult<IReadOnlyList<string>>(refs)
            : throw new PaymentProviderRejectedException("token desconhecido");

    /// <summary>O que acontece no provedor sem a loja pedir (o cliente paga o PIX, a liquidação, a validade).</summary>
    public void Set(string reference, ProviderStatus status) => _byRef[reference] = _byRef[reference] with { Status = status };

    private void ThrowIfDown()
    {
        if (Down)
        {
            throw new PaymentProviderUnavailableException("fora do ar");
        }
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
            .UseSetting("Payments:Efi:PixWebhookSecret", "segredo")
            .UseSetting("Payments:PixExpiry:Interval", "01:00:00") // os testes chamam a conferência na mão
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

/// <summary>A cobrança na SAGA (#220, #379): PIX e cartão, a liquidação pela notificação, e o dinheiro de volta.</summary>
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

    private Task OrderPlacedAsync(Guid order, long cents, OrderPaymentData? payment = null, OrderPayerData? payer = null) =>
        HandleAsync<OrderPlacedData, OnOrderPlaced>(new OrderPlacedData(order, Guid.NewGuid(), "ana@exemplo.com", new Money(cents, "BRL"), payment, payer));

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
        Assert.Equal(798, payment.FeeCents); // 3,99% em 3x na Efí, arredondado para cima
        Assert.Null((await ChargeOfAsync(order)).CardToken); // o token de uso único não fica guardado
        var approved = await LastAsync<PaymentApprovedData>(order, PaymentEvents.PaymentApproved);
        Assert.False(approved.Captured);
    }

    private async Task SettleAsync(Guid order)
    {
        var reference = (await PaymentOfAsync(order)).ProviderReference!;
        fx.Provider.Set(reference, ProviderStatus.Approved); // a Efí liquidou (paid)
        await NotifyAsync(reference);
    }

    [Fact]
    public async Task Card_IsSettled_ByTheNotification()
    {
        var order = Guid.NewGuid();
        await PlacedAndReservedAsync(order, 5000, Card("APRO"));

        await SettleAsync(order);
        await SettleAsync(order); // repetida

        var payment = await PaymentOfAsync(order);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.NotNull(payment.CapturedAt);
        Assert.Equal([PaymentEvents.PaymentApproved], await TypesOfAsync(order)); // a liquidação não confirma de novo
    }

    [Fact]
    public async Task CrashAfterPaying_ResumesWithoutChargingTwice()
    {
        var order = Guid.NewGuid();
        await OrderPlacedAsync(order, 3000, Card("APRO"));
        fx.Provider.FailAfterPayingOnce = true;
        await Assert.ThrowsAsync<PaymentProviderUnavailableException>(() => StockReservedAsync(order));

        // a referência ficou gravada antes de pagar, com o token: a mensagem volta e paga a mesma cobrança
        var payment = await PaymentOfAsync(order);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.NotNull(payment.ProviderReference);
        Assert.NotNull((await ChargeOfAsync(order)).CardToken);

        await StockReservedAsync(order);

        payment = await PaymentOfAsync(order);
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal(1, fx.Provider.Charged[payment.ProviderReference!]);
        Assert.Null((await ChargeOfAsync(order)).CardToken);
        Assert.Equal([PaymentEvents.PaymentApproved], await TypesOfAsync(order));
    }

    [Fact]
    public async Task Card_TakesPhoneAndAddressToTheProvider_WithoutKeepingThem()
    {
        var order = Guid.NewGuid();
        var address = new OrderAddressData("37200000", "Rua A", "10", null, "Centro", "Lavras", "MG");
        await OrderPlacedAsync(order, 4000, Card("APRO"), new OrderPayerData("Ana Souza", "52998224725", "35999990000", address));
        await StockReservedAsync(order);

        var payer = fx.Provider.Payers[order]!;
        Assert.Equal(("35999990000", "Lavras"), (payer.Phone, payer.Address!.City));
        var charge = await ChargeOfAsync(order);
        Assert.Null(charge.PayerPhone);
        Assert.Null(charge.PayerAddress);
    }

    public static TheoryData<string, FailureReason> Declines => new()
    {
        { "FUND", FailureReason.InsufficientFunds },
        { "FRAU", FailureReason.FraudSuspected },
        { "USADO", FailureReason.CardDeclined }, // o provedor recusa o token (4xx)
        { "SEMDADOS", FailureReason.CardDeclined }, // o pedido sem o telefone: a cobrança nem abre
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
    public async Task Pix_TakesThePayerToTheProvider_WithoutKeepingTheCpf()
    {
        var order = Guid.NewGuid();
        await OrderPlacedAsync(order, 12090, payer: new OrderPayerData("Ana Souza", "52998224725"));
        Assert.Equal("52998224725", (await ChargeOfAsync(order)).PayerCpf); // até a cobrança

        await StockReservedAsync(order);

        Assert.Equal(new Payer("Ana Souza", "52998224725"), fx.Provider.Payers[order]);
        var charge = await ChargeOfAsync(order);
        Assert.Null(charge.PayerCpf);
        Assert.Null(charge.PayerName);
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
        Assert.Equal((PaymentStatus.Approved, 144L), (payment.Status, payment.FeeCents)); // 1,19% na Efí
        Assert.True((await LastAsync<PaymentApprovedData>(paid, PaymentEvents.PaymentApproved)).Captured);

        var expired = Guid.NewGuid();
        await PlacedAndReservedAsync(expired, 5000, new OrderPaymentData(PaymentMethod.Pix, null, null, null));
        var unpaid = await PaymentOfAsync(expired);
        fx.Provider.Set(unpaid.ProviderReference!, ProviderStatus.Expired);
        await NotifyAsync(unpaid.ProviderReference!);
        Assert.Equal(FailureReason.PixExpired, (await LastAsync<PaymentFailedData>(expired, PaymentEvents.PaymentFailed)).FailureReason);
        Assert.Equal(NotificationResult.UnknownPayment, await NotifyAsync("nao-existe"));
    }

    private async Task<int> ExpirePixAsync()
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PixExpiry>().ExpireDueAsync(100, Ct);
    }

    [Fact]
    public async Task ExpiredPix_IsRemovedInTheProvider_UnlessPaidAtTheLastSecond()
    {
        // a Efí não avisa a validade que passa: o payments confere, remove a cobrança e publica pix_expired
        var unpaid = Guid.NewGuid();
        await PlacedAndReservedAsync(unpaid, FakeProvider.ExpiredPixCents, payment: null);
        var lastSecond = Guid.NewGuid();
        await PlacedAndReservedAsync(lastSecond, FakeProvider.ExpiredPixCents, payment: null);
        fx.Provider.Set((await PaymentOfAsync(lastSecond)).ProviderReference!, ProviderStatus.Approved); // o webhook não chegou

        await ExpirePixAsync();

        var expired = await PaymentOfAsync(unpaid);
        Assert.Equal((PaymentStatus.Failed, FailureReason.PixExpired), (expired.Status, expired.FailureReason));
        Assert.Contains(expired.ProviderReference!, fx.Provider.Cancelled);
        Assert.Equal(FailureReason.PixExpired, (await LastAsync<PaymentFailedData>(unpaid, PaymentEvents.PaymentFailed)).FailureReason);
        Assert.Equal(PaymentStatus.Approved, (await PaymentOfAsync(lastSecond)).Status);

        // dentro da validade, nada muda
        var valid = Guid.NewGuid();
        await PlacedAndReservedAsync(valid, 5000, payment: null);
        await ExpirePixAsync();
        Assert.Equal(PaymentStatus.Pending, (await PaymentOfAsync(valid)).Status);
    }

    [Fact]
    public async Task CancelledAfterPayment_RefundsTheCardOnSettlement_OrThePixAtOnce()
    {
        // cartão aprovado e o fornecedor recusou: a Efí não estorna antes de liquidar, então a devolução espera
        var card = Guid.NewGuid();
        await PlacedAndReservedAsync(card, 7990, Card("APRO"));
        await HandleAsync<OrderCancelledData, OnOrderCancelled>(new OrderCancelledData(card));
        await HandleAsync<OrderCancelledData, OnOrderCancelled>(new OrderCancelledData(card)); // repetido
        var waiting = await PaymentOfAsync(card);
        Assert.Equal((PaymentStatus.Authorized, "cancel"), (waiting.Status, waiting.PendingReversal));
        Assert.DoesNotContain(await EventsOfAsync(card), e => e.EventType == PaymentEvents.PaymentRefunded);

        await SettleAsync(card); // liquidou: o estorno sai agora
        await SettleAsync(card); // repetida
        var refundedCard = await PaymentOfAsync(card);
        Assert.Equal((PaymentStatus.Refunded, 7990L, (string?)null), (refundedCard.Status, refundedCard.RefundedCents, refundedCard.PendingReversal));
        var refund = await LastAsync<PaymentRefundedData>(card, PaymentEvents.PaymentRefunded);
        Assert.Equal((RefundKind.Refund, true, 7990L, "cancel"), (refund.Kind, refund.Full, refund.Amount.AmountCents, refund.Reason));
        Assert.Single(await EventsOfAsync(card), e => e.EventType == PaymentEvents.PaymentRefunded);

        // cartão ainda em análise: cancela no provedor, nada é cobrado
        var review = Guid.NewGuid();
        await PlacedAndReservedAsync(review, 2000, Card("CONT"));
        await HandleAsync<OrderCancelledData, OnOrderCancelled>(new OrderCancelledData(review));
        Assert.Equal(PaymentStatus.Voided, (await PaymentOfAsync(review)).Status);
        Assert.Empty(await EventsOfAsync(review));

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
    public async Task ReturnedOrder_RefundsThePaidAmount_Once()
    {
        // arrependimento (#223): o pedido entregue volta e o valor todo é estornado
        var order = Guid.NewGuid();
        await PlacedAndReservedAsync(order, 6430, payment: null);
        var payment = await PaymentOfAsync(order);
        fx.Provider.Set(payment.ProviderReference!, ProviderStatus.Approved);
        await NotifyAsync(payment.ProviderReference!);

        await HandleAsync<OrderReturnedData, OnOrderReturned>(new OrderReturnedData(order));
        await HandleAsync<OrderReturnedData, OnOrderReturned>(new OrderReturnedData(order)); // repetido
        payment = await PaymentOfAsync(order);
        Assert.Equal((PaymentStatus.Refunded, 6430L), (payment.Status, payment.RefundedCents));
        var refunded = await LastAsync<PaymentRefundedData>(order, PaymentEvents.PaymentRefunded);
        Assert.Equal("return", refunded.Reason); // o fulfillment não cancela a nota
        Assert.Equal((RefundKind.Refund, true, 6430L), (refunded.Kind, refunded.Full, refunded.Amount.AmountCents));
        Assert.Single(await EventsOfAsync(order), e => e.EventType == PaymentEvents.PaymentRefunded);
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
        var id = (await PaymentOfAsync(order)).Id;
        using var notSettled = await RefundAsync(id, "antes", 1000);
        Assert.Equal(HttpStatusCode.Conflict, notSettled.StatusCode); // aprovado e não liquidado: depois
        await SettleAsync(order);
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
    public async Task PixWebhook_ChecksTheSecret_AndAppliesThePayment()
    {
        var order = Guid.NewGuid();
        await PlacedAndReservedAsync(order, 5000, payment: null);
        var reference = (await PaymentOfAsync(order)).ProviderReference!;
        fx.Provider.Set(reference, ProviderStatus.Approved);
        using var http = fx.Factory.CreateClient();
        // a Efí acrescenta "/pix" à URL cadastrada, que termina em "ignorar="
        Task<HttpResponseMessage> PostAsync(string secret, object body) =>
            http.PostAsync(new Uri($"/v1/webhooks/efi/pix?hmac={secret}&ignorar=/pix", UriKind.Relative), JsonContent.Create(body), Ct);
        var pix = new { pix = new[] { new { endToEndId = "E1234567890123456789012345678901", txid = reference, valor = "50.00" } } };

        using var forged = await PostAsync("falso", pix);
        using var test = await PostAsync("segredo", new { evento = "teste" }); // o aviso de teste do cadastro
        using var signed = await PostAsync("segredo", pix);

        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        var json = await signed.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("payments.v1.yaml", "/components/responses/NotificationAck/content/application~1json/schema", json);
        Assert.Equal("applied", JsonDocument.Parse(json).RootElement.GetProperty("result").GetString());
        Assert.Equal(PaymentStatus.Approved, (await PaymentOfAsync(order)).Status);
    }

    [Fact]
    public async Task ChargeNotification_AsksTheProviderWhatChanged()
    {
        var order = Guid.NewGuid();
        await PlacedAndReservedAsync(order, 5000, Card("APRO"));
        var reference = (await PaymentOfAsync(order)).ProviderReference!;
        fx.Provider.Set(reference, ProviderStatus.Approved);
        fx.Provider.Notifications["tok-1"] = [reference];
        using var http = fx.Factory.CreateClient();
        Task<HttpResponseMessage> PostAsync(string token) => http.PostAsync(new Uri("/v1/webhooks/efi/charges", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["notification"] = token }), Ct);

        using var known = await PostAsync("tok-1");
        using var unknown = await PostAsync("tok-falso");
        using var empty = await PostAsync("");

        Assert.Equal("applied", JsonDocument.Parse(await known.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("result").GetString());
        Assert.Equal("unknown_payment", JsonDocument.Parse(await unknown.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("result").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty.StatusCode);
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
