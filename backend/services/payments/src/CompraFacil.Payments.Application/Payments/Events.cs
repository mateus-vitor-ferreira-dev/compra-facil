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
}

// ---- consumidos (só os campos usados) ----

/// <summary>O payment do OrderPlaced; ausente, o pedido é pago por PIX.</summary>
public sealed record OrderPaymentData(PaymentMethod Method, int? Installments, string? CardToken, string? CardBrand);

/// <summary>
/// Quem paga, para o provedor: nome e CPF (nulo nos pedidos de antes do #221); o telefone e o endereço de cobrança
/// (nulos nos de antes do #379), que a Efí pede no cartão.
/// </summary>
public sealed record OrderPayerData(string Name, string Cpf, string? Phone = null, OrderAddressData? BillingAddress = null)
{
    public Payer ToPayer() => new(Name, Cpf, Phone, BillingAddress is { } a
        ? new BillingAddress(a.ZipCode, a.Street, a.Number, a.Complement, a.District, a.City, a.State)
        : null);
}

/// <summary>O endereço de cobrança do OrderPlaced (o ShippingAddress do contrato).</summary>
public sealed record OrderAddressData(string ZipCode, string Street, string Number, string? Complement, string District, string City, string State);

public sealed record OrderPlacedData(
    Guid OrderId, Guid CustomerId, string? CustomerEmail, Money Total, OrderPaymentData? Payment, OrderPayerData? Payer = null);

public sealed record StockReservedData(Guid OrderId);

public sealed record OrderCancelledData(Guid OrderId);

/// <summary>data do OrderReturned (só o usado): o pedido devolvido no prazo de arrependimento.</summary>
public sealed record OrderReturnedData(Guid OrderId);

// ---- publicados ----

public sealed record PixData(string QrCode, string QrCodeBase64, string? TicketUrl, DateTimeOffset ExpiresAt)
{
    public static PixData From(PixCharge p) => new(p.QrCode, p.QrCodeBase64, p.TicketUrl, p.ExpiresAt);
}

/// <summary>PIX gerado: o cliente paga pelo QR até expires_at.</summary>
public sealed record PaymentPendingData(Guid OrderId, Guid PaymentId, Money Amount, string Provider, PaymentMethod Method, PixData Pix);

/// <summary>Pago (PIX) ou com o valor garantido (cartão autorizado): o pedido é confirmado.</summary>
public sealed record PaymentApprovedData(
    Guid OrderId, Guid PaymentId, Money Amount, string Provider, string? ProviderReference, PaymentMethod Method, int Installments, bool Captured, long FeeCents = 0);

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
