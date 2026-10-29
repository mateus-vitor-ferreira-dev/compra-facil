using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Payments.Domain;
using Microsoft.Extensions.Options;

namespace CompraFacil.Payments.Application.Payments;

/// <summary>Configuração da cobrança (seção Payments).</summary>
public sealed class PaymentOptions
{
    public const string Section = "Payments";

    /// <summary>Validade do PIX: até lá o estoque fica reservado.</summary>
    public TimeSpan PixExpiration { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// Aplica o que o provedor disse de uma cobrança (na resposta da criação ou no webhook) e agenda o evento.
/// O que não muda nada (o webhook da nossa própria captura, um repetido) não publica.
/// </summary>
public sealed class PaymentOutcome(IOutbox outbox, IOptions<FeeOptions> fees, TimeProvider clock)
{
    public bool Apply(Payment payment, ProviderPayment result)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(result);
        var now = clock.GetUtcNow();
        var fee = fees.Value.FeeCents(payment.Method, payment.Installments, payment.Amount.AmountCents);
        switch (result.Status)
        {
            case ProviderStatus.Pending when payment.Status == PaymentStatus.Processing && result.Pix is { } pix:
                payment.MarkPending(result.Reference, pix, now);
                outbox.Add(PaymentEvents.Topic, Key(payment), PaymentEvents.PaymentPending,
                    new PaymentPendingData(payment.OrderId, payment.Id, payment.Amount, payment.Provider, payment.Method, PixData.From(pix)));
                return true;
            case ProviderStatus.Authorized when payment.Status == PaymentStatus.Processing:
                payment.Authorize(result.Reference, result.CardLastFour, fee, now);
                Approved(payment, captured: false);
                return true;
            case ProviderStatus.Approved when payment.IsOpen:
                payment.Approve(result.Reference, fee, now);
                Approved(payment, captured: true);
                return true;
            case ProviderStatus.Approved when payment.Status == PaymentStatus.Authorized:
                payment.Capture(now); // capturado (pela nossa chamada, a notificação chega depois)
                return true;
            case ProviderStatus.Rejected when payment.IsOpen:
                Failed(payment, result.FailureReason ?? FailureReason.CardDeclined, result.Reference, now);
                return true;
            case ProviderStatus.InProcess when payment.Status == PaymentStatus.Processing:
                return payment.Track(result.Reference, now); // em análise: o webhook decide
            case ProviderStatus.Expired when payment.IsOpen:
                Failed(payment, FailureReason.PixExpired, result.Reference, now);
                return true;
            default:
                return false; // em análise, ou o que já foi aplicado
        }
    }

    /// <summary>Recusado sem chegar a ter uma cobrança no provedor.</summary>
    public void Reject(Payment payment, FailureReason reason)
    {
        ArgumentNullException.ThrowIfNull(payment);
        Failed(payment, reason, null, clock.GetUtcNow());
    }

    private void Approved(Payment p, bool captured) =>
        outbox.Add(PaymentEvents.Topic, Key(p), PaymentEvents.PaymentApproved,
            new PaymentApprovedData(p.OrderId, p.Id, p.Amount, p.Provider, p.ProviderReference, p.Method, p.Installments, captured));

    private void Failed(Payment p, FailureReason reason, string? reference, DateTimeOffset now)
    {
        p.Fail(reason, reference, now);
        outbox.Add(PaymentEvents.Topic, Key(p), PaymentEvents.PaymentFailed,
            new PaymentFailedData(p.OrderId, p.Id, p.Amount, p.Provider, reason));
    }

    public void Refunded(Payment p, long amountCents, RefundKind kind) =>
        outbox.Add(PaymentEvents.Topic, Key(p), PaymentEvents.PaymentRefunded,
            new PaymentRefundedData(p.OrderId, p.Id, new Money(amountCents, p.Amount.Currency), p.RefundedCents, p.Provider,
                p.RefundReference, p.RefundedAt!.Value, kind, kind == RefundKind.Void || p.Status == PaymentStatus.Refunded));

    private static string Key(Payment p) => p.OrderId.ToString();
}

/// <summary>
/// Registra um fato sobre o pedido e cobra se já tiver tudo (valor + estoque garantido). PIX: gera o QR
/// (PaymentPending) e espera o webhook. Cartão: autoriza sem capturar (PaymentApproved com captured=false);
/// a captura vem quando o fornecedor aceita o pedido.
/// </summary>
public sealed class ChargeProcessor(
    IPaymentRepository payments, IPaymentProvider provider, PaymentOutcome outcome, IOptions<PaymentOptions> options, TimeProvider clock)
{
    public Task ApplyAsync(Guid orderId, Action<Charge, DateTimeOffset> record, CancellationToken ct) =>
        OptimisticConcurrency.RetryAsync(payments.Reset, async () =>
        {
            var now = clock.GetUtcNow();
            var charge = await payments.GetChargeAsync(orderId, ct);
            if (charge is null)
            {
                charge = Charge.For(orderId, now);
                payments.Add(charge);
            }
            record(charge, now);
            Payment? payment = null;
            if (charge.ReadyToCharge)
            {
                payment = await ChargeAsync(charge, now, ct);
            }
            await payments.SaveChangesAsync(ct);
            if (payment is not null)
            {
                PaymentsMetrics.Charges.Add(1, new KeyValuePair<string, object?>("method", payment.Method.ToString()),
                    new KeyValuePair<string, object?>("result", payment.Status.ToString()),
                    new KeyValuePair<string, object?>("reason", payment.FailureReason?.ToString() ?? "-"));
            }
            return true;
        });

    private async Task<Payment> ChargeAsync(Charge charge, DateTimeOffset now, CancellationToken ct)
    {
        var amount = charge.Amount!.Value;
        var (token, brand) = (charge.CardToken, charge.CardBrand);
        var payment = Payment.Start(Guid.CreateVersion7(), charge.OrderId, amount, charge.Method, charge.Installments, provider.Name, now);
        charge.AttachPayment(payment.Id, now);
        payments.Add(payment);

        // falha temporária sobe: nada é gravado e a mensagem é tentada de novo, com a mesma chave
        var key = charge.OrderId.ToString();
        try
        {
            var result = charge.Method == PaymentMethod.Pix
                ? await provider.CreatePixAsync(new PixRequest(key, charge.OrderId, amount, charge.CustomerEmail, now + options.Value.PixExpiration), ct)
                : await provider.AuthorizeCardAsync(new CardRequest(key, charge.OrderId, amount, charge.CustomerEmail, token!, brand!, charge.Installments), ct);
            outcome.Apply(payment, result);
        }
        catch (PaymentProviderRejectedException) when (charge.Method == PaymentMethod.Card)
        {
            // token já usado ou inválido: para o cliente, é o cartão recusado (o pedido é cancelado com o motivo)
            outcome.Reject(payment, FailureReason.CardDeclined);
        }
        return payment;
    }
}

/// <summary>OrderPlaced → guarda valor, cliente e como pagar (e cobra se o estoque já foi reservado).</summary>
public sealed class OnOrderPlaced(ChargeProcessor processor) : IHandleEvent<OrderPlacedData>
{
    public Task HandleAsync(OrderPlacedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        var p = data.Payment;
        var instruction = p is null
            ? PaymentInstruction.Pix // pedido sem o meio de pagamento: PIX, pago na página do pedido
            : new PaymentInstruction(p.Method, p.Installments ?? 1, p.CardToken, p.CardBrand);
        return processor.ApplyAsync(data.OrderId, (charge, now) =>
        {
            if (charge.Amount is null)
            {
                try
                {
                    charge.RecordOrder(data.CustomerId, data.CustomerEmail, data.Total, instruction, now);
                }
                catch (DomainException e)
                {
                    throw new PoisonMessageException(e.Message, e); // fora do contrato: tentar de novo não conserta
                }
            }
        }, ct);
    }
}

/// <summary>StockReserved → estoque garantido (e cobra se o OrderPlaced já chegou).</summary>
public sealed class OnStockReserved(ChargeProcessor processor) : IHandleEvent<StockReservedData>
{
    public Task HandleAsync(StockReservedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return processor.ApplyAsync(data.OrderId, (charge, now) => charge.RecordStockReserved(now), ct);
    }
}

/// <summary>
/// SupplierOrderPlaced → o fornecedor aceitou: a autorização do cartão é capturada. Até aqui, um pedido que o
/// fornecedor recusasse só teria a autorização cancelada, sem estorno nem tarifa.
/// </summary>
public sealed class OnSupplierOrderPlaced(IPaymentRepository payments, IPaymentProvider provider, PaymentOutcome outcome) : IHandleEvent<SupplierOrderPlacedData>
{
    public Task HandleAsync(SupplierOrderPlacedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return OptimisticConcurrency.RetryAsync(payments.Reset, async () =>
        {
            var payment = await payments.GetPaidForUpdateAsync(data.OrderId, ct);
            if (payment is not { Status: PaymentStatus.Authorized })
            {
                return false; // PIX (já pago), ou capturado antes (evento repetido)
            }
            outcome.Apply(payment, await provider.CaptureAsync(payment.ProviderReference!, ct));
            await payments.SaveChangesAsync(ct);
            PaymentsMetrics.Captures.Add(1);
            return true;
        });
    }
}

/// <summary>
/// OrderCancelled → descarta a cobrança que ainda não aconteceu; com o pedido já pago (o fornecedor não
/// atendeu), devolve o dinheiro: cancela a autorização do cartão (nada é cobrado) ou estorna o PIX pago
/// (e o cartão já capturado). Publica PaymentRefunded. Chave do estorno: o payment_id.
/// </summary>
public sealed class OnOrderCancelled(IPaymentRepository payments, IPaymentProvider provider, PaymentOutcome outcome, TimeProvider clock)
    : IHandleEvent<OrderCancelledData>
{
    public Task HandleAsync(OrderCancelledData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return OptimisticConcurrency.RetryAsync(payments.Reset, async () =>
        {
            var now = clock.GetUtcNow();
            var charge = await payments.GetChargeAsync(data.OrderId, ct);
            if (charge is null)
            {
                charge = Charge.For(data.OrderId, now);
                payments.Add(charge);
            }
            charge.Cancel(now);

            var payment = charge.PaymentId is { } id ? await payments.GetPaymentForUpdateAsync(id, ct) : null;
            string? kind = null;
            switch (payment?.Status)
            {
                case PaymentStatus.Authorized or PaymentStatus.Pending:
                    // falha temporária sobe: a mensagem é tentada de novo (cancelar de novo devolve o mesmo)
                    await provider.CancelAsync(payment.ProviderReference!, ct);
                    var wasAuthorized = payment.Status == PaymentStatus.Authorized;
                    payment.Void(clock.GetUtcNow());
                    if (wasAuthorized)
                    {
                        outcome.Refunded(payment, payment.Amount.AmountCents, RefundKind.Void);
                        kind = "void";
                    }
                    break;
                case PaymentStatus.Approved:
                    var left = payment.Amount.AmountCents - payment.RefundedCents;
                    var refund = await provider.RefundAsync(payment.ProviderReference!, left, $"{payment.Id}:cancel", ct);
                    if (payment.Refund(refund.AmountCents, $"{payment.Id}:cancel", refund.Reference, clock.GetUtcNow()))
                    {
                        outcome.Refunded(payment, refund.AmountCents, RefundKind.Refund);
                        kind = "refund";
                    }
                    break;
            }
            await payments.SaveChangesAsync(ct);
            if (kind is not null)
            {
                PaymentsMetrics.Refunds.Add(1, new KeyValuePair<string, object?>("kind", kind));
            }
            return true;
        });
    }
}
