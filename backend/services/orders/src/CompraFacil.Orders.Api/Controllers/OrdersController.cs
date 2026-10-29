using System.Security.Claims;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Orders.Application.Orders;
using CompraFacil.Orders.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Orders.Api.Controllers;

/// <summary>
/// Pedidos (contracts/openapi/orders.v1.yaml). O cliente vem do access token do identity: <c>sub</c> é
/// o cliente e <c>email</c> o destino dos avisos; o corpo só traz os itens.
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class OrdersController(PlaceOrder placeOrder, OrderQueries queries) : ControllerBase
{
    /// <summary>placeOrder: cria o pedido e inicia a SAGA; responde 202 com o pedido pending.</summary>
    [HttpPost("v1/orders")]
    [Authorize(Policy = JwtAuth.CustomerPolicy)]
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
        if (CustomerId() is not { } customerId)
        {
            return Forbid();
        }

        var result = await placeOrder.HandleAsync(new PlaceOrderCommand(
            customerId,
            User.FindFirstValue("email") ?? "",
            [.. (body.Items ?? []).Select(i => new PlaceOrderItem(i.ProductId ?? Guid.Empty, i.Quantity ?? 0, i.ExpectedUnitPriceCents))],
            idempotencyKey,
            body.Payment is { } p
                ? new PlaceOrderPayment(p.Method ?? (PaymentMethod)(-1), p.Installments ?? 1, p.CardToken, p.CardBrand)
                : null), ct);

        var order = result.Order;
        return Accepted($"/v1/orders/{order.Id}", OrderResponse.From(order));
    }

    /// <summary>getOrder: detalhe e situação do pedido, para o dono ou um operador.</summary>
    [HttpGet("v1/orders/{order_id:guid}")]
    [Authorize]
    public async Task<IActionResult> Get([FromRoute(Name = "order_id")] Guid orderId, CancellationToken ct)
    {
        var isOperator = User.IsInRole("admin");
        if (!isOperator && !User.IsInRole("customer"))
        {
            return Forbid();
        }
        return Ok(OrderResponse.From(await queries.GetAsync(orderId, new OrderReader(CustomerId(), isOperator), ct)));
    }

    /// <summary>listMyOrders: os pedidos do cliente do token, do mais recente para o mais antigo.</summary>
    [HttpGet("v1/me/orders")]
    [Authorize(Policy = JwtAuth.CustomerPolicy)]
    public async Task<IActionResult> ListMine(
        [FromQuery(Name = "status")] OrderStatus? status,
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        CancellationToken ct)
    {
        if (CustomerId() is not { } customerId)
        {
            return Forbid();
        }
        var page = await queries.ListAsync(
            new ListOrdersQuery(customerId, status, cursor, limit ?? ListOrdersQuery.DefaultLimit), ct);
        return Ok(new OrderListResponse(
            [.. page.Items.Select(OrderResponse.From)],
            new PageInfo(page.NextCursor, HasMore: page.NextCursor is not null)));
    }

    // o sub do identity é o id da conta (UUID); outro formato não é um cliente desta loja
    private Guid? CustomerId() => Guid.TryParse(User.FindFirstValue("sub"), out var id) && id != Guid.Empty ? id : null;
}
