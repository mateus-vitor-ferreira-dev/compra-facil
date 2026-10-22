using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Inventory.Application.Stock;
using CompraFacil.Inventory.Domain;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Inventory.Api.Controllers;

/// <summary>Saldo, reservas e ajustes (contracts/openapi/inventory.v1.yaml).</summary>
[ApiController]
[Produces("application/json")]
public sealed class StockController(StockQueries queries, AdjustStock adjustStock) : ControllerBase
{
    /// <summary>listStock: saldo de vários produtos; sem registro aparece zerado.</summary>
    [HttpGet("v1/stock")]
    public async Task<StockListResponse> List([FromQuery(Name = "product_ids")] string? productIds, CancellationToken ct)
    {
        var ids = new List<Guid>();
        foreach (var raw in (productIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ids.Add(Guid.TryParse(raw, out var id)
                ? id
                : throw new ValidationException([new ValidationFailure("ProductIds", $"'{raw}' não é um UUID")]));
        }
        return new StockListResponse(await queries.ListAsync(ids, ct));
    }

    /// <summary>getStock: saldo de um produto, com ETag da versão.</summary>
    [HttpGet("v1/stock/{product_id:guid}")]
    public async Task<StockLevel> Get([FromRoute(Name = "product_id")] Guid productId, CancellationToken ct)
    {
        var level = await queries.GetAsync(productId, ct);
        Response.Headers.ETag = $"\"{level.UpdatedAt.UtcTicks}\"";
        return level;
    }

    /// <summary>getReservation: a reserva de um pedido (depuração da SAGA).</summary>
    [HttpGet("v1/reservations/{order_id:guid}")]
    public async Task<ReservationResponse> GetReservation([FromRoute(Name = "order_id")] Guid orderId, CancellationToken ct) =>
        ReservationResponse.From(await queries.GetReservationAsync(orderId, ct));

    /// <summary>adjustStock: soma delta ao estoque físico (role admin).</summary>
    [HttpPost("v1/admin/stock/{product_id:guid}/adjustments")]
    [Authorize(Policy = AdminAuth.Policy)]
    public async Task<IActionResult> Adjust(
        [FromRoute(Name = "product_id")] Guid productId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        StockAdjustmentRequest body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Requisição malformada",
                detail: "o header Idempotency-Key é obrigatório");
        }
        var level = await adjustStock.HandleAsync(new AdjustStockCommand(
            productId, body.Delta ?? 0, body.Reason ?? "", body.Note, idempotencyKey, User.Identity?.Name ?? ""), ct);
        return Ok(level);
    }
}

public sealed record StockListResponse(IReadOnlyList<StockLevel> Data);

public sealed record StockAdjustmentRequest(int? Delta, string? Reason, string? Note);

public sealed record ReservationResponse(
    Guid OrderId, ReservationStatus Status, IReadOnlyList<ReservedItem> Items, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static ReservationResponse From(Reservation r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new(r.OrderId, r.Status, r.Items, r.CreatedAt, r.UpdatedAt);
    }
}
