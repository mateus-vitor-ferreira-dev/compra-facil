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
/// As notificações do provedor (o webhook do PIX, o token do cartão) só dizem qual cobrança mudou: a verdade vem
/// da consulta a ele. Repetida ou atrasada não muda nada. Depois de aplicar, a devolução que esperava a
/// liquidação do cartão sai.
/// </summary>
public sealed partial class ProviderNotifications(
    IPaymentRepository payments, IPaymentProvider provider, PaymentOutcome outcome, PaymentReversal reversal, ILogger<ProviderNotifications> logger)
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
            var result = await provider.GetAsync(reference, ct);
            if (result.Status == ProviderStatus.Disputed)
            {
                // o dinheiro está em disputa: quem responde é a equipe, no painel do provedor, dentro do prazo dele
                LogDisputed(logger, payment.OrderId, reference);
                PaymentsMetrics.Disputes.Add(1);
            }
            var changed = outcome.Apply(payment, result);
            var kind = await reversal.ResumeAsync(payment, ct);
            await payments.SaveChangesAsync(ct);
            if (kind is not null)
            {
                PaymentsMetrics.Refunds.Add(1, new KeyValuePair<string, object?>("kind", kind));
            }
            return changed || kind is not null ? NotificationResult.Applied : NotificationResult.Ignored;
        });

    /// <summary>A notificação do cartão (só o token): aplica cada cobrança que o provedor diz ter mudado.</summary>
    public async Task<NotificationResult> ApplyTokenAsync(string token, CancellationToken ct)
    {
        var result = NotificationResult.Ignored;
        foreach (var reference in await provider.ChangedByNotificationAsync(token, ct))
        {
            var one = await ApplyAsync(reference, ct);
            result = one == NotificationResult.Applied || result == NotificationResult.Applied ? NotificationResult.Applied
                : one == NotificationResult.UnknownPayment ? NotificationResult.UnknownPayment : result;
        }
        return result;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "notificação do provedor para uma cobrança desconhecida {Reference}")]
    private static partial void LogUnknown(ILogger logger, string reference);

    [LoggerMessage(Level = LogLevel.Error, Message = "pagamento do pedido {OrderId} contestado no provedor ({Reference}): responder no painel do provedor")]
    private static partial void LogDisputed(ILogger logger, Guid orderId, string reference);
}

/// <summary>
/// O PIX que venceu sem pagar (#379): a Efí não avisa a validade que passa (a cobrança continua ATIVA, só não se
/// paga mais). O payments confere o que venceu: pago no último instante, aprova; senão, remove a cobrança no
/// provedor, para ninguém pagar depois, e publica PaymentFailed (pix_expired).
/// </summary>
public sealed class PixExpiry(IPaymentRepository payments, IPaymentProvider provider, PaymentOutcome outcome, TimeProvider clock)
{
    /// <summary>Folga depois da validade: o pagamento feito no último segundo ainda chega pelo webhook.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    public async Task<int> ExpireDueAsync(int limit, CancellationToken ct)
    {
        var due = await payments.ListPendingPixDueAsync(clock.GetUtcNow() - Grace, limit, ct);
        foreach (var id in due)
        {
            await ExpireAsync(id, ct);
        }
        return due.Count;
    }

    public Task ExpireAsync(Guid paymentId, CancellationToken ct) =>
        OptimisticConcurrency.RetryAsync(payments.Reset, async () =>
        {
            var payment = await payments.GetPaymentForUpdateAsync(paymentId, ct);
            if (payment is not { Status: PaymentStatus.Pending, ProviderReference: { } reference })
            {
                return false;
            }
            var result = await provider.GetAsync(reference, ct);
            if (result.Status is ProviderStatus.Pending or ProviderStatus.Expired)
            {
                try
                {
                    result = await provider.CancelAsync(reference, ct);
                }
                catch (PaymentProviderRejectedException)
                {
                    result = await provider.GetAsync(reference, ct); // pago agora mesmo, ou já removido
                }
            }
            outcome.Apply(payment, result.Status is ProviderStatus.Pending or ProviderStatus.Cancelled or ProviderStatus.Expired
                ? result with { Status = ProviderStatus.Expired }
                : result);
            await payments.SaveChangesAsync(ct);
            return true;
        });
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
                throw new DomainException(payment.Status switch
                {
                    PaymentStatus.Authorized => "o cartão ainda não foi liquidado pelo provedor: o estorno fica disponível depois da liquidação",
                    not PaymentStatus.Approved => $"o pagamento está {payment.Status}: só o aprovado se estorna",
                    _ => $"o estorno vai de 1 a {left} centavos",
                });
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
