using Embrulhado.Payments.Domain;
using Microsoft.Extensions.Logging;

namespace Embrulhado.Payments.Application.Payments;

public enum RefundResult
{
    /// <summary>O provedor devolveu, e o estorno foi aplicado agora (PaymentRefunded agendado).</summary>
    Applied,

    /// <summary>O estorno desta chave já estava aplicado.</summary>
    Repeated,

    /// <summary>Pedido ao provedor e sem confirmação: o pedido fica gravado, e a equipe confere no painel dele.</summary>
    Unconfirmed,
}

/// <summary>
/// O estorno no provedor, pedido uma vez só (F-007). O do cartão não tem chave de idempotência na Efí, e o pedido
/// era feito dentro da nova tentativa da concorrência otimista, antes de gravar: um conflito, ou o payments caindo
/// depois de a Efí aceitar, pedia de novo. Agora o pedido (<see cref="Payment.PendingRefund"/>) é gravado antes de
/// chamar o provedor; a resposta que chegou fica guardada nesta execução (o conflito depois dela não pede de novo), e
/// o pedido que ficou sem resposta é conferido no provedor (<see cref="IPaymentProvider.ResumeRefundAsync"/>).
/// Grava no meio (o pedido, e o pedido recusado): quem chama está numa tentativa da concorrência otimista.
/// </summary>
public sealed partial class ProviderRefunds(
    IPaymentRepository payments, IPaymentProvider provider, PaymentOutcome outcome, TimeProvider clock, ILogger<ProviderRefunds> logger)
{
    private readonly Dictionary<string, ProviderRefund> _accepted = [];
    private readonly Dictionary<string, PaymentProviderRejectedException> _rejected = [];

    /// <summary>
    /// Estorna <paramref name="amountCents"/> com a chave <paramref name="key"/>: aplica no pagamento e agenda
    /// PaymentRefunded (com o <paramref name="reason"/>, o da devolução do pedido).
    /// </summary>
    /// <exception cref="DomainException">Outro estorno ainda sem confirmação, ou o valor não cabe.</exception>
    /// <exception cref="PaymentProviderRejectedException">O provedor recusou: nada foi devolvido.</exception>
    public async Task<RefundResult> RefundAsync(Payment payment, long amountCents, string key, string? reason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (payment.RefundOf(key) is not null)
        {
            return RefundResult.Repeated; // a mesma requisição de novo: o estorno já foi feito
        }
        if (payment.PendingRefund?.Key == key)
        {
            return await ResumeAsync(payment, ct);
        }
        payment.RequestRefund(key, amountCents, reason, clock.GetUtcNow());
        await payments.SaveChangesAsync(ct); // o conflito aqui ainda não pediu nada ao provedor
        ProviderRefund refund;
        try
        {
            refund = await provider.RefundAsync(payment.ProviderReference!, amountCents, key, ct);
        }
        catch (PaymentProviderRejectedException e)
        {
            _rejected[key] = e;
            await DropAsync(payment, ct);
            throw;
        }
        _accepted[key] = refund;
        return Apply(payment, key, refund, reason);
    }

    /// <summary>
    /// O estorno pedido e ainda não aplicado (<see cref="Payment.PendingRefund"/>): o que esta execução já sabe, ou o
    /// que o provedor confirma. Sem pedido esperando, <see cref="RefundResult.Repeated"/>.
    /// </summary>
    public async Task<RefundResult> ResumeAsync(Payment payment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (payment.PendingRefund is not { } pending)
        {
            return RefundResult.Repeated;
        }
        if (_rejected.TryGetValue(pending.Key, out var rejected))
        {
            await DropAsync(payment, ct);
            throw rejected;
        }
        if (!_accepted.TryGetValue(pending.Key, out var refund))
        {
            ProviderRefund? confirmed;
            try
            {
                confirmed = await provider.ResumeRefundAsync(payment.ProviderReference!, pending.AmountCents, pending.Key, pending.RequestedAt, ct);
            }
            catch (PaymentProviderRejectedException e)
            {
                _rejected[pending.Key] = e;
                await DropAsync(payment, ct);
                throw;
            }
            if (confirmed is null)
            {
                LogUnconfirmed(logger, payment.OrderId, payment.Id, pending.Key, pending.AmountCents, pending.RequestedAt);
                PaymentsMetrics.RefundsUnconfirmed.Add(1);
                return RefundResult.Unconfirmed;
            }
            _accepted[pending.Key] = refund = confirmed;
        }
        return Apply(payment, pending.Key, refund, pending.Reason);
    }

    private RefundResult Apply(Payment payment, string key, ProviderRefund refund, string? reason)
    {
        if (!payment.Refund(refund.AmountCents, key, refund.Reference, clock.GetUtcNow()))
        {
            return RefundResult.Repeated;
        }
        outcome.Refunded(payment, refund.AmountCents, RefundKind.Refund, reason);
        return RefundResult.Applied;
    }

    private async Task DropAsync(Payment payment, CancellationToken ct)
    {
        payment.DropPendingRefund(clock.GetUtcNow());
        await payments.SaveChangesAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "estorno {Key} do pedido {OrderId} (pagamento {PaymentId}, {AmountCents} centavos, pedido em {RequestedAt}) sem confirmação do provedor: conferir no painel dele")]
    private static partial void LogUnconfirmed(ILogger logger, Guid orderId, Guid paymentId, string key, long amountCents, DateTimeOffset requestedAt);
}
