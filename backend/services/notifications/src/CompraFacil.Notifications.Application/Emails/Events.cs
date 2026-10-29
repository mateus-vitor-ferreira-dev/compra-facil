namespace CompraFacil.Notifications.Application.Emails;

/// <summary>Eventos consumidos (contracts/asyncapi/notifications.v1.yaml).</summary>
public static class NotificationEvents
{
    public const string OrdersTopic = "orders.order.v1";
    public const string OrderConfirmed = nameof(OrderConfirmed);
    public const string OrderCancelled = nameof(OrderCancelled);
    public const string OrderReturned = nameof(OrderReturned);
    public const string OrderRefunded = nameof(OrderRefunded);

    public const string InvoicesTopic = "fulfillment.invoice.v1";
    public const string InvoiceIssued = nameof(InvoiceIssued);

    public const string AccountsTopic = "identity.account.v1";
    public const string EmailConfirmationRequested = nameof(EmailConfirmationRequested);
}

public sealed record Money(long AmountCents, string Currency);

public sealed record OrderItem(string Name, int Quantity, Money UnitPrice, Money LineTotal);

public sealed record OrderConfirmedData(Guid OrderId, string CustomerEmail, IReadOnlyList<OrderItem> Items, Money Total);

/// <summary>data do OrderCancelled (só os campos usados no e-mail).</summary>
/// <param name="OrderId">Pedido cancelado.</param>
/// <param name="CustomerEmail">Destino do e-mail.</param>
/// <param name="Reason">out_of_stock | payment_failed | supplier_failed | invoice_failed.</param>
/// <param name="PaymentFailureReason">card_declined, insufficient_funds, ... (só em payment_failed).</param>
public sealed record OrderCancelledData(Guid OrderId, string CustomerEmail, string Reason, string? PaymentFailureReason);

/// <summary>data do EmailConfirmationRequested (contracts/asyncapi/identity.v1.yaml).</summary>
public sealed record EmailConfirmationRequestedData(Guid AccountId, string Email, string Name, Uri ConfirmationUrl, DateTimeOffset ExpiresAt);

/// <summary>data do InvoiceIssued (contracts/asyncapi/fulfillment.v1.yaml, só os campos usados no e-mail).</summary>
public sealed record InvoiceIssuedData(Guid OrderId, string CustomerEmail, string AccessKey, int Number, int Series, long TotalCents);

/// <summary>Seção Store: o endereço público da loja, para os links dos e-mails.</summary>
public sealed class StoreOptions
{
    public const string Section = "Store";
    public Uri BaseUrl { get; set; } = new("http://localhost:3000/");
}

/// <summary>data do OrderReturned (só os campos usados no e-mail): a devolução no prazo de arrependimento.</summary>
public sealed record OrderReturnedData(Guid OrderId, string CustomerEmail, Money Total);

/// <summary>data do OrderRefunded: o estorno feito; reason: returned | supplier_failed | invoice_failed.</summary>
public sealed record OrderRefundedData(Guid OrderId, string CustomerEmail, Money Amount, string Reason);
