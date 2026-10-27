using CompraFacil.Orders.Application.Orders;
using CompraFacil.Orders.Domain;

namespace CompraFacil.Orders.Api.Controllers;

// Corpos HTTP do contrato (contracts/openapi/orders.v1.yaml). Campos opcionais aqui para a
// ausência virar 422 do validador (com o nome do campo), e não um 400 genérico de binding.

// o cliente e o e-mail vêm do token; customer_id/customer_email de clientes antigos são ignorados
public sealed record PlaceOrderRequest(IReadOnlyList<PlaceOrderItemRequest>? Items);

public sealed record PlaceOrderItemRequest(Guid? ProductId, int? Quantity, long? ExpectedUnitPriceCents);

public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    OrderStatus Status,
    CancellationReason? CancellationReason,
    IReadOnlyList<OrderItemData> Items,
    Money Total,
    Guid? PaymentId,
    DateTimeOffset? StockReservedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static OrderResponse From(Order o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new(o.Id, o.CustomerId, o.Status, o.CancellationReason, [.. o.Items.Select(OrderItemData.From)],
            o.Total, o.PaymentId, o.StockReservedAt, o.CreatedAt, o.UpdatedAt);
    }
}

public sealed record PageInfo(string? NextCursor, bool HasMore);

public sealed record OrderListResponse(IReadOnlyList<OrderResponse> Data, PageInfo Page);
