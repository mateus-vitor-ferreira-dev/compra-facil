using CompraFacil.Orders.Application.Orders;
using CompraFacil.Orders.Domain;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Orders.Api.Controllers;

/// <summary>Pedidos (contracts/openapi/orders.v1.yaml).</summary>
[ApiController]
[Route("v1/orders")]
[Produces("application/json")]
public sealed class OrdersController(PlaceOrder placeOrder, OrderQueries queries) : ControllerBase
{
    /// <summary>placeOrder: cria o pedido e inicia a SAGA; responde 202 com o pedido pending.</summary>
    [HttpPost]
    public async Task<IActionResult> Place(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        PlaceOrderRequest body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Requisição malformada",
                detail: "o header Idempotency-Key é obrigatório");
        }

        var result = await placeOrder.HandleAsync(new PlaceOrderCommand(
            body.CustomerId ?? Guid.Empty,
            body.CustomerEmail ?? "",
            [.. (body.Items ?? []).Select(i => new PlaceOrderItem(i.ProductId ?? Guid.Empty, i.Quantity ?? 0, i.ExpectedUnitPriceCents))],
            idempotencyKey), ct);

        var order = result.Order;
        return Accepted($"/v1/orders/{order.Id}", OrderResponse.From(order));
    }

    /// <summary>getOrder: detalhe e situação do pedido.</summary>
    [HttpGet("{order_id:guid}")]
    public async Task<OrderResponse> Get([FromRoute(Name = "order_id")] Guid orderId, CancellationToken ct) =>
        OrderResponse.From(await queries.GetAsync(orderId, ct));

    /// <summary>listOrders: pedidos de um cliente, do mais recente para o mais antigo.</summary>
    [HttpGet]
    public async Task<OrderListResponse> List(
        [FromQuery(Name = "customer_id")] Guid? customerId,
        [FromQuery(Name = "status")] OrderStatus? status,
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        CancellationToken ct)
    {
        var page = await queries.ListAsync(
            new ListOrdersQuery(customerId ?? Guid.Empty, status, cursor, limit ?? ListOrdersQuery.DefaultLimit), ct);
        return new OrderListResponse(
            [.. page.Items.Select(OrderResponse.From)],
            new PageInfo(page.NextCursor, HasMore: page.NextCursor is not null));
    }
}
