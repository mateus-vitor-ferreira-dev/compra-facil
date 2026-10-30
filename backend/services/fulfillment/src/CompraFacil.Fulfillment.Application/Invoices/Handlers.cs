using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Domain;
using Microsoft.Extensions.Logging;

namespace CompraFacil.Fulfillment.Application.Invoices;

/// <summary>
/// PaymentRefunded com o pedido inteiro devolvido: a nota autorizada precisa ser cancelada. Só marca, na
/// transação do evento; quem chama o emissor é o <see cref="InvoiceCanceller"/> (um serviço externo não
/// prende o consumer). Na desistência da compra não cancela: a mercadoria foi entregue, e a nota de venda
/// segue valendo, com a de devolução (<see cref="ReturnInvoiceIssuer"/>).
/// </summary>
public sealed class OnPaymentRefunded(IInvoiceRepository invoices, TimeProvider clock) : IHandleEvent<PaymentRefundedData>
{
    public async Task HandleAsync(PaymentRefundedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!data.Full || data.Reason == InvoiceEvents.ReturnRefund)
        {
            return; // estorno de parte (o painel) ou desistência: a nota do pedido continua valendo
        }
        if (await invoices.GetAsync(data.OrderId, ct) is { } invoice)
        {
            invoice.RequestCancel(clock.GetUtcNow());
        }
    }
}

/// <summary>Emite a nota de devolução de um pedido e publica o InvoiceReturnIssued; quem chama grava.</summary>
public sealed partial class ReturnInvoiceIssuer(IInvoiceIssuer issuer, IOutbox outbox, ILogger<ReturnInvoiceIssuer> logger)
{
    public async Task<InvoiceChange> IssueAsync(Invoice invoice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        switch (await issuer.IssueReturnAsync(invoice, ct))
        {
            case InvoiceIssue.Authorized a:
                var change = invoice.MarkReturnIssued(a.ProviderId, a.AccessKey, a.Number, a.Series, a.IssuedAt);
                outbox.Publish(invoice, change);
                FulfillmentMetrics.Invoice("return_issued");
                return change;
            case InvoiceIssue.Rejected r:
                invoice.MarkReturnFailed($"{r.Code}: {r.Reason}");
                LogRejected(logger, invoice.Id, r.Code, r.Reason);
                FulfillmentMetrics.Invoice("return_rejected");
                return InvoiceChange.None;
            default:
                throw new InvalidOperationException("resposta do emissor desconhecida");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "a SEFAZ rejeitou a nota de devolução do pedido {OrderId} ({Code}: {Reason}); a equipe precisa resolver")]
    private static partial void LogRejected(ILogger logger, Guid orderId, int code, string reason);
}

/// <summary>Cancela no emissor uma nota marcada pelo estorno e publica o InvoiceCancelled; quem chama grava.</summary>
public sealed partial class InvoiceCanceller(IInvoiceIssuer issuer, IOutbox outbox, TimeProvider clock, ILogger<InvoiceCanceller> logger)
{
    public const string Justification = "Venda cancelada: o pedido não foi atendido e o valor foi estornado ao cliente";

    public async Task<InvoiceChange> CancelAsync(Invoice invoice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        try
        {
            await issuer.CancelAsync(invoice.ProviderId!, Justification, ct);
        }
        catch (InvoiceCancelRefusedException e)
        {
            invoice.MarkCancelFailed(e.Message);
            LogRefused(logger, invoice.Id, e.Message);
            FulfillmentMetrics.InvoiceCancelRefused();
            return InvoiceChange.None;
        }
        var change = invoice.MarkCancelled(clock.GetUtcNow());
        outbox.Publish(invoice, change);
        return change;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "o emissor não cancelou a nota do pedido {OrderId}: {Reason}; a equipe precisa resolver")]
    private static partial void LogRefused(ILogger logger, Guid orderId, string reason);
}
