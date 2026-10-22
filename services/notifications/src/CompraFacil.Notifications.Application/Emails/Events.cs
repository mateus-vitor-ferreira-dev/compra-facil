namespace CompraFacil.Notifications.Application.Emails;

/// <summary>Eventos consumidos (contracts/asyncapi/notifications.v1.yaml).</summary>
public static class NotificationEvents
{
    public const string OrdersTopic = "orders.order.v1";
    public const string OrderConfirmed = nameof(OrderConfirmed);
    public const string OrderCancelled = nameof(OrderCancelled);
}

public sealed record Money(long AmountCents, string Currency);

public sealed record OrderItem(string Name, int Quantity, Money UnitPrice, Money LineTotal);

public sealed record OrderConfirmedData(Guid OrderId, string CustomerEmail, IReadOnlyList<OrderItem> Items, Money Total);

/// <summary>data do OrderCancelled (só os campos usados no e-mail).</summary>
/// <param name="OrderId">Pedido cancelado.</param>
/// <param name="CustomerEmail">Destino do e-mail.</param>
/// <param name="Reason">out_of_stock | payment_failed.</param>
/// <param name="PaymentFailureReason">card_declined, insufficient_funds, ... (só em payment_failed).</param>
public sealed record OrderCancelledData(Guid OrderId, string CustomerEmail, string Reason, string? PaymentFailureReason);
