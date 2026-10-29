using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Fulfillment.Api.Controllers;

/// <summary>listOverdueSupplierOrders (contracts/openapi/fulfillment.v1.yaml): os pedidos parados no fornecedor.</summary>
[ApiController]
[Produces("application/json")]
[Authorize(Policy = JwtAuth.AdminPolicy)]
public sealed class AdminSupplierOrdersController(OverdueOrders overdue) : ControllerBase
{
    [HttpGet("v1/admin/supplier-orders/overdue")]
    public async Task<OverdueSupplierOrderList> ListOverdue([FromQuery] int? limit, CancellationToken ct)
    {
        var n = limit ?? 50;
        if (n is < 1 or > OverdueOrders.MaxLimit)
        {
            throw new ValidationException([new ValidationFailure("Limit", $"deve estar entre 1 e {OverdueOrders.MaxLimit}")]);
        }
        return new OverdueSupplierOrderList([.. (await overdue.ListAsync(n, ct)).Select(OverdueSupplierOrder.From)]);
    }
}

public sealed record OverdueSupplierOrder(
    Guid OrderId,
    SupplierOrderStatus Status,
    OverdueStage Stage,
    DateTimeOffset DueAt,
    string Supplier,
    string? SupplierOrderId,
    string CustomerEmail,
    int Attempts,
    string? LastError,
    Tracking? Tracking,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PlacedAt,
    DateTimeOffset? ShippedAt)
{
    public static OverdueSupplierOrder From(OverdueOrder overdue)
    {
        ArgumentNullException.ThrowIfNull(overdue);
        var o = overdue.Order;
        return new(o.Id, o.Status, overdue.Stage, overdue.DueAt, o.Supplier, o.SupplierOrderId, o.CustomerEmail, o.Attempts,
            o.LastError, o.Tracking, o.CreatedAt, o.PlacedAt, o.ShippedAt);
    }
}

public sealed record OverdueSupplierOrderList(IReadOnlyList<OverdueSupplierOrder> Data);
