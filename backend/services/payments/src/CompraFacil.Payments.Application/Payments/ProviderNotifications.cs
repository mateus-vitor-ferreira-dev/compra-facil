using CompraFacil.BuildingBlocks.Data;
using CompraFacil.Payments.Domain;
using Microsoft.Extensions.Logging;

namespace CompraFacil.Payments.Application.Payments;

public enum NotificationResult
{
    Applied,
    Ignored,
    UnknownPayment,
}

/// <summary>
/// A notificação do provedor (o webhook, já com a assinatura conferida) só diz qual cobrança mudou: a
/// verdade vem da consulta a ele, como o Mercado Pago recomenda. Repetida ou atrasada não muda nada.
/// </summary>
public sealed partial class ProviderNotifications(IPaymentRepository payments, IPaymentProvider provider, PaymentOutcome outcome, ILogger<ProviderNotifications> logger)
{
    public Task<NotificationResult> ApplyAsync(string reference, CancellationToken ct) =>
        OptimisticConcurrency.RetryAsync(payments.Reset, async () =>
        {
            var payment = await payments.GetByReferenceForUpdateAsync(provider.Name, reference, ct);
            if (payment is null)
            {
                LogUnknown(logger, reference);
                return NotificationResult.UnknownPayment;
            }
            var changed = outcome.Apply(payment, await provider.GetAsync(reference, ct));
            await payments.SaveChangesAsync(ct);
            return changed ? NotificationResult.Applied : NotificationResult.Ignored;
        });

    [LoggerMessage(Level = LogLevel.Warning, Message = "notificação do provedor para uma cobrança desconhecida {Reference}")]
    private static partial void LogUnknown(ILogger logger, string reference);
}

/// <summary>
/// Estorno pelo painel (#220): parte ou o resto de um pagamento aprovado, idempotente pela chave da
/// requisição (a mesma devolve o mesmo estorno). Publica PaymentRefunded.
/// </summary>
public sealed class RefundPayment(IPaymentRepository payments, IPaymentProvider provider, PaymentOutcome outcome, TimeProvider clock)
{
    /// <exception cref="BuildingBlocks.Web.NotFoundException">Pagamento inexistente.</exception>
    /// <exception cref="DomainException">Não está aprovado, ou o valor passa do que falta devolver.</exception>
    public Task<Payment> HandleAsync(Guid paymentId, long? amountCents, string idempotencyKey, CancellationToken ct) =>
        OptimisticConcurrency.RetryAsync(payments.Reset, async () =>
        {
            var payment = await payments.GetPaymentForUpdateAsync(paymentId, ct)
                ?? throw new BuildingBlocks.Web.NotFoundException($"pagamento {paymentId} não existe");
            var key = $"{payment.Id}:{idempotencyKey}";
            if (payment.RefundOf(key) is not null)
            {
                return payment; // a mesma requisição de novo: o estorno já foi feito
            }
            var left = payment.Amount.AmountCents - payment.RefundedCents;
            var amount = amountCents ?? left;
            if (payment.Status != PaymentStatus.Approved || amount <= 0 || amount > left)
            {
                throw new DomainException(payment.Status != PaymentStatus.Approved
                    ? $"o pagamento está {payment.Status}: só o aprovado se estorna"
                    : $"o estorno vai de 1 a {left} centavos");
            }
            var refund = await provider.RefundAsync(payment.ProviderReference!, amount, key, ct);
            if (payment.Refund(refund.AmountCents, key, refund.Reference, clock.GetUtcNow()))
            {
                outcome.Refunded(payment, refund.AmountCents, RefundKind.Refund);
                PaymentsMetrics.Refunds.Add(1, new KeyValuePair<string, object?>("kind", "partial"));
            }
            await payments.SaveChangesAsync(ct);
            return payment;
        });
}
