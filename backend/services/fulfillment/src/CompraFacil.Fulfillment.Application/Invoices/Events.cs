using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application.Invoices;

/// <summary>Os eventos da nota fiscal (contracts/asyncapi/fulfillment.v1.yaml, tópico fulfillment.invoice.v1).</summary>
public static class InvoiceEvents
{
    public const string Topic = "fulfillment.invoice.v1";
    public const string InvoiceIssued = nameof(InvoiceIssued);
    public const string InvoiceRejected = nameof(InvoiceRejected);
    public const string InvoiceCancelled = nameof(InvoiceCancelled);
    public const string InvoiceReturnIssued = nameof(InvoiceReturnIssued);

    public const string PaymentsTopic = "payments.payment.v1";
    public const string PaymentRefunded = nameof(PaymentRefunded);

    /// <summary>O motivo do estorno quando o cliente desiste da compra (PaymentRefunded.reason).</summary>
    public const string ReturnRefund = "return";

    public const string OrderReturned = nameof(OrderReturned);
}

public sealed record InvoiceIssuedData(
    Guid OrderId, Guid CustomerId, string CustomerEmail, string AccessKey, int Number, int Series, long TotalCents, DateTimeOffset IssuedAt);

public sealed record InvoiceRejectedData(Guid OrderId, int Code, string Reason, DateTimeOffset RejectedAt);

public sealed record InvoiceCancelledData(Guid OrderId, string AccessKey, DateTimeOffset CancelledAt);

public sealed record InvoiceReturnIssuedData(
    Guid OrderId, string AccessKey, string ReferencedAccessKey, int Number, int Series, long TotalCents, DateTimeOffset IssuedAt);

/// <summary>data do PaymentRefunded (só os campos usados): full = o pedido inteiro voltou ao cliente; reason = por quê.</summary>
public sealed record PaymentRefundedData(Guid OrderId, bool Full, string? Reason = null);

/// <summary>data do OrderReturned (só os campos usados): o cliente desistiu da compra.</summary>
public sealed record OrderReturnedData(Guid OrderId);

internal static class InvoiceEventsExtensions
{
    /// <summary>Agenda no outbox o evento da mudança (nenhum, se nada mudou). Chave = order_id.</summary>
    public static void Publish(this IOutbox outbox, Invoice invoice, InvoiceChange change)
    {
        var key = invoice.Id.ToString();
        switch (change)
        {
            case InvoiceChange.Issued:
                outbox.Add(InvoiceEvents.Topic, key, InvoiceEvents.InvoiceIssued, new InvoiceIssuedData(
                    invoice.Id, invoice.CustomerId, invoice.CustomerEmail, invoice.AccessKey!, invoice.Number!.Value,
                    invoice.Series!.Value, invoice.TotalCents, invoice.IssuedAt!.Value));
                break;
            case InvoiceChange.Rejected:
                outbox.Add(InvoiceEvents.Topic, key, InvoiceEvents.InvoiceRejected, new InvoiceRejectedData(
                    invoice.Id, invoice.RejectionCode!.Value, invoice.RejectionReason!, invoice.RejectedAt!.Value));
                break;
            case InvoiceChange.Cancelled:
                outbox.Add(InvoiceEvents.Topic, key, InvoiceEvents.InvoiceCancelled, new InvoiceCancelledData(
                    invoice.Id, invoice.AccessKey!, invoice.CancelledAt!.Value));
                break;
            case InvoiceChange.ReturnIssued:
                outbox.Add(InvoiceEvents.Topic, key, InvoiceEvents.InvoiceReturnIssued, new InvoiceReturnIssuedData(
                    invoice.Id, invoice.ReturnAccessKey!, invoice.AccessKey!, invoice.ReturnNumber!.Value,
                    invoice.ReturnSeries!.Value, invoice.TotalCents, invoice.ReturnIssuedAt!.Value));
                break;
            case InvoiceChange.None:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, null);
        }
    }
}
