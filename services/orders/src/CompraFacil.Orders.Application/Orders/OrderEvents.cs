using CompraFacil.Orders.Domain;

namespace CompraFacil.Orders.Application.Orders;

/// <summary>Eventos do orders (contracts/asyncapi/orders.v1.yaml, tópico orders.order.v1).</summary>
public static class OrderEvents
{
    public const string Topic = "orders.order.v1";
    public const string OrderPlaced = nameof(OrderPlaced);
}

/// <summary>Item do pedido nos eventos (OrderItem do contrato REST).</summary>
public sealed record OrderItemData(
    Guid ProductId, string Sku, string Name, int Quantity, Money UnitPrice, Money LineTotal, Guid? CampaignId)
{
    public static OrderItemData From(OrderItem i) => new(i.ProductId, i.Sku, i.Name, i.Quantity, i.UnitPrice, i.LineTotal, i.CampaignId);
}

/// <summary>data do OrderPlaced: início da SAGA (inventory reserva, payments guarda o valor).</summary>
public sealed record OrderPlacedData(Guid OrderId, Guid CustomerId, IReadOnlyList<OrderItemData> Items, Money Total)
{
    public static OrderPlacedData From(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new(order.Id, order.CustomerId, [.. order.Items.Select(OrderItemData.From)], order.Total);
    }
}
