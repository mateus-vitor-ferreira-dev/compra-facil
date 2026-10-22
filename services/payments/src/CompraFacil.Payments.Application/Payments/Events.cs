using CompraFacil.Payments.Domain;

namespace CompraFacil.Payments.Application.Payments;

/// <summary>Tópicos e eventos (contracts/asyncapi/payments.v1.yaml).</summary>
public static class PaymentEvents
{
    public const string Topic = "payments.payment.v1";
    public const string PaymentApproved = nameof(PaymentApproved);
    public const string PaymentFailed = nameof(PaymentFailed);

    public const string OrdersTopic = "orders.order.v1";
    public const string ReservationsTopic = "inventory.reservation.v1";
}

// ---- consumidos (só os campos usados) ----

public sealed record OrderPlacedData(Guid OrderId, Guid CustomerId, Money Total);

public sealed record StockReservedData(Guid OrderId);

public sealed record OrderCancelledData(Guid OrderId);

// ---- publicados ----

public sealed record PaymentApprovedData(Guid OrderId, Guid PaymentId, Money Amount, string Provider, string? ProviderReference);

public sealed record PaymentFailedData(Guid OrderId, Guid PaymentId, Money Amount, string Provider, FailureReason FailureReason);
