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
                payment.Capture(now); // o cartão aprovado foi liquidado (a notificação de paid)
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
            new PaymentApprovedData(p.OrderId, p.Id, p.Amount, p.Provider, p.ProviderReference, p.Method, p.Installments, captured, p.FeeCents));

    private void Failed(Payment p, FailureReason reason, string? reference, DateTimeOffset now)
    {
        p.Fail(reason, reference, now);
        outbox.Add(PaymentEvents.Topic, Key(p), PaymentEvents.PaymentFailed,
            new PaymentFailedData(p.OrderId, p.Id, p.Amount, p.Provider, reason));
    }

    public void Refunded(Payment p, long amountCents, RefundKind kind, string? reason = null) =>
        outbox.Add(PaymentEvents.Topic, Key(p), PaymentEvents.PaymentRefunded,
            new PaymentRefundedData(p.OrderId, p.Id, new Money(amountCents, p.Amount.Currency), p.RefundedCents, p.Provider,
                p.RefundReference, p.RefundedAt!.Value, kind, kind == RefundKind.Void || p.Status == PaymentStatus.Refunded, reason));

    private static string Key(Payment p) => p.OrderId.ToString();
}

/// <summary>
/// Registra um fato sobre o pedido e cobra se já tiver tudo (valor + estoque garantido). PIX: gera o QR
/// (PaymentPending) e espera o webhook. Cartão: em dois passos (#379), porque a Efí não tem chave de
/// idempotência: abre a cobrança no provedor, grava a referência e só então paga; se o payments cair no meio, a
/// nova tentativa paga a mesma cobrança (e a paga de novo não cobra outra vez). Aprovado, publica PaymentApproved
/// com captured=false; a liquidação chega pela notificação.
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
            else if (charge is { PaymentId: { } id, CardToken: not null, Cancelled: false })
            {
                payment = await ResumeCardAsync(charge, id, ct); // o payments caiu entre abrir a cobrança e pagar
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
        var payment = Payment.Start(Guid.CreateVersion7(), charge.OrderId, amount, charge.Method, charge.Installments, provider.Name, now);
        charge.AttachPayment(payment.Id, now);
        payments.Add(payment);

        // falha temporária sobe: nada é gravado e a mensagem é tentada de novo
        if (charge.Method == PaymentMethod.Pix)
        {
            var pix = await provider.CreatePixAsync(
                new PixRequest(charge.OrderId.ToString(), charge.OrderId, amount, charge.CustomerEmail, now + options.Value.PixExpiration, charge.Payer), ct);
            charge.Charged(now);
            outcome.Apply(payment, pix);
            return payment;
        }

        string reference;
        try
        {
            reference = await provider.OpenCardChargeAsync(CardRequestOf(charge), ct);
        }
        catch (PaymentProviderRejectedException)
        {
            // dados que o provedor não aceita (o pedido de antes do #379, sem o telefone): para o cliente, o cartão recusado
            charge.Charged(now);
            outcome.Reject(payment, FailureReason.CardDeclined);
            return payment;
        }
        payment.Track(reference, now);
        await payments.SaveChangesAsync(ct); // a referência gravada antes de cobrar: a nova tentativa paga a mesma
        return await PayAsync(charge, payment, ct);
    }

    private async Task<Payment?> ResumeCardAsync(Charge charge, Guid paymentId, CancellationToken ct)
    {
        var payment = await payments.GetPaymentForUpdateAsync(paymentId, ct);
        if (payment is { Status: PaymentStatus.Processing, ProviderReference: not null })
        {
            return await PayAsync(charge, payment, ct);
        }
        charge.Charged(clock.GetUtcNow()); // já decidido: o token não serve mais
        return null;
    }

    private async Task<Payment> PayAsync(Charge charge, Payment payment, CancellationToken ct)
    {
        try
        {
            outcome.Apply(payment, await provider.PayCardAsync(payment.ProviderReference!, CardRequestOf(charge), ct));
        }
        catch (PaymentProviderRejectedException)
        {
            // token já usado ou inválido: para o cliente, é o cartão recusado (o pedido é cancelado com o motivo)
            outcome.Reject(payment, FailureReason.CardDeclined);
        }
        charge.Charged(clock.GetUtcNow());
        return payment;
    }

    private static CardRequest CardRequestOf(Charge charge) =>
        new(charge.OrderId.ToString(), charge.OrderId, charge.Amount!.Value, charge.CustomerEmail, charge.CardToken!, charge.CardBrand!,
            charge.Installments, charge.Payer);
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
                    charge.RecordOrder(data.CustomerId, data.CustomerEmail, data.Total, instruction, now, data.Payer?.ToPayer());
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
/// OrderCancelled → descarta a cobrança que ainda não aconteceu; com o pedido já pago (o fornecedor não atendeu),
/// devolve o dinheiro (<see cref="PaymentReversal"/>). Publica PaymentRefunded. Chave do estorno: o payment_id.
/// </summary>
public sealed class OnOrderCancelled(IPaymentRepository payments, PaymentReversal reversal, TimeProvider clock)
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
            if (payment is not null)
            {
                charge.Charged(now); // entre os dois passos do cartão: não se paga mais
            }
            var kind = payment is null ? null : await reversal.ReverseAsync(payment, "cancel", ct);
            await payments.SaveChangesAsync(ct);
            if (kind is not null)
            {
                PaymentsMetrics.Refunds.Add(1, new KeyValuePair<string, object?>("kind", kind));
            }
            return true;
        });
    }
}

/// <summary>
/// Devolve o dinheiro de um pagamento, conforme onde ele está:
/// <list type="bullet">
/// <item>PIX não pago ou cartão ainda em análise: cancela no provedor (nada é cobrado);</item>
/// <item>cartão aprovado e não liquidado: a Efí não cancela nem estorna nessa janela, então a devolução fica
/// esperando (<see cref="Payment.PendingReversal"/>) e sai quando a liquidação chega (<see cref="ResumeAsync"/>);</item>
/// <item>pago: estorna tudo o que falta (com o frete).</item>
/// </list>
/// Publica PaymentRefunded quando o dinheiro volta. A chave do estorno é o payment_id com o motivo, então a mensagem
/// repetida não estorna duas vezes. Devolve o tipo ("void", "refund", "deferred"), ou nulo se não havia o que devolver.
/// </summary>
public sealed class PaymentReversal(IPaymentProvider provider, PaymentOutcome outcome, TimeProvider clock)
{
    public async Task<string?> ReverseAsync(Payment payment, string reason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (payment is { Status: PaymentStatus.Pending or PaymentStatus.Processing, ProviderReference: { } reference })
        {
            // falha temporária sobe: a mensagem é tentada de novo (cancelar de novo devolve o mesmo)
            try
            {
                await provider.CancelAsync(reference, ct);
                payment.Void(clock.GetUtcNow());
                return null; // nada foi cobrado, e o pedido nunca soube de pagamento: não há o que avisar
            }
            catch (PaymentProviderRejectedException)
            {
                // já não se cancela: o cliente pagou o PIX, ou o cartão saiu da análise. A verdade vem do provedor
                outcome.Apply(payment, await provider.GetAsync(reference, ct));
            }
        }
        switch (payment.Status)
        {
            case PaymentStatus.Authorized:
                return payment.DeferReversal(reason, clock.GetUtcNow()) ? "deferred" : null;
            case PaymentStatus.Approved:
                var left = payment.Amount.AmountCents - payment.RefundedCents;
                var key = $"{payment.Id}:{reason}";
                var refund = await provider.RefundAsync(payment.ProviderReference!, left, key, ct);
                payment.ClearPendingReversal(clock.GetUtcNow());
                if (payment.Refund(refund.AmountCents, key, refund.Reference, clock.GetUtcNow()))
                {
                    outcome.Refunded(payment, refund.AmountCents, RefundKind.Refund, reason);
                    return "refund";
                }
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Depois de o provedor mudar a cobrança: a devolução que esperava a liquidação sai agora; se o cartão acabou
    /// recusado, não há mais o que devolver.
    /// </summary>
    public async Task<string?> ResumeAsync(Payment payment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (payment.PendingReversal is not { } reason)
        {
            return null;
        }
        if (payment.Status == PaymentStatus.Approved)
        {
            return await ReverseAsync(payment, reason, ct);
        }
        if (payment.Status is not (PaymentStatus.Processing or PaymentStatus.Authorized))
        {
            payment.ClearPendingReversal(clock.GetUtcNow());
        }
        return null;
    }
}

/// <summary>
/// OrderReturned → o cliente se arrependeu (CDC, art. 49): o dinheiro volta na hora, todo, com o frete, sem
/// esperar a devolução ao fornecedor. Publica PaymentRefunded (full), que o orders marca no pedido.
/// </summary>
public sealed class OnOrderReturned(IPaymentRepository payments, PaymentReversal reversal) : IHandleEvent<OrderReturnedData>
{
    public Task HandleAsync(OrderReturnedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return OptimisticConcurrency.RetryAsync(payments.Reset, async () =>
        {
            var charge = await payments.GetChargeAsync(data.OrderId, ct);
            var payment = charge?.PaymentId is { } id ? await payments.GetPaymentForUpdateAsync(id, ct) : null;
            var kind = payment is null ? null : await reversal.ReverseAsync(payment, "return", ct);
            await payments.SaveChangesAsync(ct);
            if (kind is not null)
            {
                PaymentsMetrics.Refunds.Add(1, new KeyValuePair<string, object?>("kind", kind));
            }
            return true;
        });
    }
}
