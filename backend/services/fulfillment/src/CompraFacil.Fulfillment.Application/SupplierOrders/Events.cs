using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application.SupplierOrders;

/// <summary>Tópicos e tipos de evento (contracts/asyncapi/fulfillment.v1.yaml).</summary>
public static class FulfillmentEvents
{
    public const string Topic = "fulfillment.supplier-order.v1";
    public const string SupplierOrderPlaced = nameof(SupplierOrderPlaced);
    public const string SupplierOrderShipped = nameof(SupplierOrderShipped);
    public const string SupplierOrderDelivered = nameof(SupplierOrderDelivered);
    public const string SupplierOrderFailed = nameof(SupplierOrderFailed);

    public const string OrdersTopic = "orders.order.v1";
    public const string OrderConfirmed = nameof(OrderConfirmed);
}

public sealed record Money(long AmountCents, string Currency);

public sealed record OrderItem(Guid ProductId, string Sku, string Name, int Quantity, Money UnitPrice, Money LineTotal);

public sealed record Recipient(string Name, string Cpf, string Phone);

public sealed record ShippingAddress(string ZipCode, string Street, string Number, string? Complement, string District, string City, string State);

/// <summary>data do OrderConfirmed (só os campos usados): o pedido pago que vai ao fornecedor, para quem e para onde.</summary>
/// <summary>O frete do pedido (#222): o preço vai na nota; a data prometida é o prazo do fornecedor.</summary>
public sealed record OrderShipping(string Service, Money Price, DateOnly PromisedBy);

public sealed record OrderConfirmedData(
    Guid OrderId, Guid CustomerId, string CustomerEmail, IReadOnlyList<OrderItem> Items, Money Total,
    Recipient? Recipient = null, ShippingAddress? ShippingAddress = null, string? PaymentMethod = null, OrderShipping? Shipping = null)
{
    /// <summary>O destino do envio; nulo no pedido de antes do #221 (sem endereço).</summary>
    public ShipTo? ShipTo => Recipient is { } r && ShippingAddress is { } a
        ? new ShipTo(r.Name, r.Cpf, r.Phone, a.ZipCode, a.Street, a.Number, a.Complement, a.District, a.City, a.State)
        : null;
}

public sealed record SupplierOrderPlacedData(Guid OrderId, string Supplier, string SupplierOrderId, DateTimeOffset PlacedAt);

public sealed record SupplierOrderShippedData(
    Guid OrderId, string Supplier, string? SupplierOrderId, string TrackingNumber, string? TrackingProvider, string? TrackingUrl, DateTimeOffset ShippedAt);

public sealed record SupplierOrderDeliveredData(Guid OrderId, string Supplier, string? SupplierOrderId, DateTimeOffset DeliveredAt);

/// <summary>O pedido não chegou ao fornecedor; reason: out_of_stock | rejected | supplier_cancelled | supplier_unavailable.</summary>
public sealed record SupplierOrderFailedData(Guid OrderId, string Supplier, string? SupplierOrderId, string Reason, string? Detail, DateTimeOffset FailedAt);

internal static class SupplierOrderEventsExtensions
{
    /// <summary>Agenda no outbox o evento da mudança (nenhum, se nada mudou). Chave = order_id.</summary>
    public static void Publish(this BuildingBlocks.Messaging.IOutbox outbox, SupplierOrder order, SupplierOrderChange change)
    {
        var key = order.Id.ToString();
        switch (change)
        {
            case SupplierOrderChange.Placed:
                outbox.Add(FulfillmentEvents.Topic, key, FulfillmentEvents.SupplierOrderPlaced,
                    new SupplierOrderPlacedData(order.Id, order.Supplier, order.SupplierOrderId ?? "", order.PlacedAt!.Value));
                break;
            case SupplierOrderChange.Shipped:
                outbox.Add(FulfillmentEvents.Topic, key, FulfillmentEvents.SupplierOrderShipped,
                    new SupplierOrderShippedData(order.Id, order.Supplier, order.SupplierOrderId, order.Tracking!.Number,
                        order.Tracking.Provider, order.Tracking.Url, order.ShippedAt!.Value));
                break;
            case SupplierOrderChange.Delivered:
                outbox.Add(FulfillmentEvents.Topic, key, FulfillmentEvents.SupplierOrderDelivered,
                    new SupplierOrderDeliveredData(order.Id, order.Supplier, order.SupplierOrderId, order.DeliveredAt!.Value));
                break;
            case SupplierOrderChange.Failed:
                outbox.Add(FulfillmentEvents.Topic, key, FulfillmentEvents.SupplierOrderFailed,
                    new SupplierOrderFailedData(order.Id, order.Supplier, order.SupplierOrderId, order.FailureReason!, order.FailureDetail, order.FailedAt!.Value));
                break;
            case SupplierOrderChange.None:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, null);
        }
    }
}
