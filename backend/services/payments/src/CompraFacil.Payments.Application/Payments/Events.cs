using CompraFacil.Payments.Domain;

namespace CompraFacil.Payments.Application.Payments;

/// <summary>Tópicos e eventos (contracts/asyncapi/payments.v1.yaml).</summary>
public static class PaymentEvents
{
    public const string Topic = "payments.payment.v1";
    public const string PaymentPending = nameof(PaymentPending);
    public const string PaymentApproved = nameof(PaymentApproved);
    public const string PaymentFailed = nameof(PaymentFailed);
    public const string PaymentRefunded = nameof(PaymentRefunded);

    public const string OrdersTopic = "orders.order.v1";
    public const string ReservationsTopic = "inventory.reservation.v1";
    public const string FulfillmentTopic = "fulfillment.supplier-order.v1";
}

// ---- consumidos (só os campos usados) ----

/// <summary>O payment do OrderPlaced; ausente, o pedido é pago por PIX.</summary>
public sealed record OrderPaymentData(PaymentMethod Method, int? Installments, string? CardToken, string? CardBrand);

public sealed record OrderPlacedData(Guid OrderId, Guid CustomerId, string? CustomerEmail, Money Total, OrderPaymentData? Payment);

public sealed record StockReservedData(Guid OrderId);

public sealed record OrderCancelledData(Guid OrderId);

/// <summary>data do OrderReturned (só o usado): o pedido devolvido no prazo de arrependimento.</summary>
public sealed record OrderReturnedData(Guid OrderId);

public sealed record SupplierOrderPlacedData(Guid OrderId);

// ---- publicados ----

public sealed record PixData(string QrCode, string QrCodeBase64, string? TicketUrl, DateTimeOffset ExpiresAt)
{
    public static PixData From(PixCharge p) => new(p.QrCode, p.QrCodeBase64, p.TicketUrl, p.ExpiresAt);
}

/// <summary>PIX gerado: o cliente paga pelo QR até expires_at.</summary>
public sealed record PaymentPendingData(Guid OrderId, Guid PaymentId, Money Amount, string Provider, PaymentMethod Method, PixData Pix);

/// <summary>Pago (PIX) ou com o valor garantido (cartão autorizado): o pedido é confirmado.</summary>
public sealed record PaymentApprovedData(
    Guid OrderId, Guid PaymentId, Money Amount, string Provider, string? ProviderReference, PaymentMethod Method, int Installments, bool Captured);

public sealed record PaymentFailedData(Guid OrderId, Guid PaymentId, Money Amount, string Provider, FailureReason FailureReason);

/// <summary>
/// Dinheiro de volta: Kind refund (estorno do que foi pago, total ou parte) ou void (a autorização foi
/// cancelada: nada chegou a ser cobrado). Full: o pedido inteiro voltou. Reason: por que voltou (return: o
/// cliente desistiu da compra, e o fulfillment não cancela a nota); nulo no estorno pelo painel.
/// </summary>
public sealed record PaymentRefundedData(
    Guid OrderId, Guid PaymentId, Money Amount, long TotalRefundedCents, string Provider, string? RefundReference,
    DateTimeOffset RefundedAt, RefundKind Kind, bool Full, string? Reason = null);

public enum RefundKind
{
    Refund,
    Void,
}
