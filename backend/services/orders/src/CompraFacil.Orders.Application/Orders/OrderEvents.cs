using CompraFacil.Orders.Domain;

namespace CompraFacil.Orders.Application.Orders;

/// <summary>Eventos do orders (contracts/asyncapi/orders.v1.yaml, tópico orders.order.v1).</summary>
public static class OrderEvents
{
    public const string Topic = "orders.order.v1";
    public const string OrderPlaced = nameof(OrderPlaced);
    public const string OrderConfirmed = nameof(OrderConfirmed);
    public const string OrderCancelled = nameof(OrderCancelled);
    public const string OrderShipped = nameof(OrderShipped);
    public const string OrderDelivered = nameof(OrderDelivered);

    public const string ReservationsTopic = "inventory.reservation.v1";
    public const string PaymentsTopic = "payments.payment.v1";
    public const string PaymentPending = nameof(PaymentPending);
    public const string FulfillmentTopic = "fulfillment.supplier-order.v1";
}

/// <summary>Item do pedido nos eventos (OrderItem do contrato REST).</summary>
public sealed record OrderItemData(
    Guid ProductId, string Sku, string Name, int Quantity, Money UnitPrice, Money LineTotal, Guid? CampaignId)
{
    public static OrderItemData From(OrderItem i) => new(i.ProductId, i.Sku, i.Name, i.Quantity, i.UnitPrice, i.LineTotal, i.CampaignId);
}

/// <summary>Como pagar, para o payments: o token do cartão (de uso único) só passa por aqui, não fica no pedido.</summary>
public sealed record OrderPaymentData(PaymentMethod Method, int Installments, string? CardToken, string? CardBrand);

/// <summary>data do OrderPlaced: início da SAGA (inventory reserva, payments guarda o valor e como cobrar).</summary>
public sealed record OrderPlacedData(
    Guid OrderId, Guid CustomerId, string CustomerEmail, IReadOnlyList<OrderItemData> Items, Money Total, OrderPaymentData Payment)
{
    public static OrderPlacedData From(Order order, string? cardToken = null, string? cardBrand = null)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(order.Id, order.CustomerId, order.CustomerEmail, [.. order.Items.Select(OrderItemData.From)], order.Total,
            new OrderPaymentData(order.PaymentMethod, order.Installments, cardToken, cardBrand));
    }
}

/// <summary>data do OrderConfirmed: fim feliz (inventory dá baixa, notifications avisa o cliente).</summary>
public sealed record OrderConfirmedData(
    Guid OrderId, Guid CustomerId, string CustomerEmail, Guid PaymentId, IReadOnlyList<OrderItemData> Items, Money Total)
{
    public static OrderConfirmedData From(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(order.Id, order.CustomerId, order.CustomerEmail, order.PaymentId!.Value,
            [.. order.Items.Select(OrderItemData.From)], order.Total);
    }
}

/// <summary>data do OrderCancelled: compensação (inventory libera, payments descarta, notifications avisa).</summary>
public sealed record OrderCancelledData(
    Guid OrderId, Guid CustomerId, string CustomerEmail, CancellationReason Reason, string? PaymentFailureReason)
{
    public static OrderCancelledData From(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(order.Id, order.CustomerId, order.CustomerEmail, order.CancellationReason!.Value, order.PaymentFailureReason);
    }
}

/// <summary>data do OrderShipped: o fornecedor despachou (o cliente acompanha pelo rastreio).</summary>
public sealed record OrderShippedData(Guid OrderId, Guid CustomerId, string CustomerEmail, Tracking Tracking, DateTimeOffset ShippedAt)
{
    public static OrderShippedData From(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(order.Id, order.CustomerId, order.CustomerEmail, order.Tracking!, order.ShippedAt!.Value);
    }
}

/// <summary>data do OrderDelivered: chegou ao cliente.</summary>
public sealed record OrderDeliveredData(Guid OrderId, Guid CustomerId, string CustomerEmail, DateTimeOffset DeliveredAt)
{
    public static OrderDeliveredData From(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(order.Id, order.CustomerId, order.CustomerEmail, order.DeliveredAt!.Value);
    }
}

// ---- consumidos dos outros serviços (só os campos usados) ----

public sealed record StockReservedData(Guid OrderId);

public sealed record StockReservationFailedData(Guid OrderId);

public sealed record PaymentApprovedData(Guid OrderId, Guid PaymentId);

public sealed record PaymentFailedData(Guid OrderId, Guid PaymentId, string FailureReason);

public sealed record PaymentRefundedData(Guid OrderId, Guid PaymentId, bool? Full);

public sealed record PixData(string QrCode, string QrCodeBase64, string? TicketUrl, DateTimeOffset ExpiresAt);

public sealed record PaymentPendingData(Guid OrderId, Guid PaymentId, PixData Pix);

public sealed record SupplierOrderPlacedData(Guid OrderId);

public sealed record SupplierOrderShippedData(Guid OrderId, string TrackingNumber, string? TrackingProvider, string? TrackingUrl);

public sealed record SupplierOrderDeliveredData(Guid OrderId);

public sealed record SupplierOrderFailedData(Guid OrderId, string Reason);
