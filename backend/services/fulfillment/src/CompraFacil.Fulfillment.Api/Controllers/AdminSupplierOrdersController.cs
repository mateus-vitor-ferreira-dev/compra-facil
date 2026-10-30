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
public sealed class AdminSupplierOrdersController(OverdueOrders overdue, PendingSupplierReturns returns) : ControllerBase
{
    /// <summary>listSupplierReturns: as devoluções ao fornecedor que pedem atenção (#223).</summary>
    [HttpGet("v1/admin/supplier-returns")]
    public async Task<SupplierReturnList> ListReturns([FromQuery] int? limit, CancellationToken ct)
    {
        var n = limit ?? 50;
        if (n is < 1 or > PendingSupplierReturns.MaxLimit)
        {
            throw new ValidationException([new ValidationFailure("Limit", $"deve estar entre 1 e {PendingSupplierReturns.MaxLimit}")]);
        }
        return new SupplierReturnList([.. (await returns.ListAsync(n, ct)).Select(SupplierReturnItem.From)]);
    }

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

public sealed record SupplierReturnItem(
    Guid OrderId,
    SupplierReturnStatus Status,
    bool Overdue,
    DateTimeOffset DueAt,
    string Supplier,
    string? SupplierOrderId,
    string? DisputeId,
    string? ReturnCode,
    string CustomerEmail,
    IReadOnlyList<SupplierOrderItem> Items,
    int Attempts,
    string? LastError,
    string? ResolutionReason,
    string? InvoiceError,
    DateTimeOffset RequestedAt,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? ResolvedAt)
{
    public static SupplierReturnItem From(PendingSupplierReturn pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var r = pending.Return;
        return new(r.Id, r.Status, pending.Overdue, pending.DueAt, r.Supplier, r.SupplierOrderId, r.DisputeId, r.ReturnCode, r.CustomerEmail,
            r.Items, r.Attempts, r.LastError, r.ResolutionReason, pending.InvoiceError, r.RequestedAt, r.OpenedAt, r.ResolvedAt);
    }
}

public sealed record SupplierReturnList(IReadOnlyList<SupplierReturnItem> Data);

public sealed record OverdueSupplierOrderList(IReadOnlyList<OverdueSupplierOrder> Data);
