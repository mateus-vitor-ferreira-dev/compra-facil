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
public sealed class AdminSupplierOrdersController(
    OverdueOrders overdue, PendingSupplierReturns returns, SupplierOrderActions actions, SupplierReturnActions returnActions,
    OrderMargins margins) : ControllerBase
{
    /// <summary>listOrderMargins: a margem real dos pedidos mais recentes (#225).</summary>
    [HttpGet("v1/admin/order-margins")]
    public async Task<OrderMarginList> ListMargins([FromQuery] int? limit, CancellationToken ct)
    {
        var n = limit ?? 50;
        if (n is < 1 or > OrderMargins.MaxLimit)
        {
            throw new ValidationException([new ValidationFailure("Limit", $"deve estar entre 1 e {OrderMargins.MaxLimit}")]);
        }
        return new OrderMarginList([.. (await margins.ListAsync(n, ct)).Select(OrderMarginItem.From)]);
    }

    private const int MaxFailed = 200;

    /// <summary>listFailedSupplierOrders: os que falharam nos últimos 30 dias (#225).</summary>
    [HttpGet("v1/admin/supplier-orders/failed")]
    public async Task<FailedSupplierOrderList> ListFailed([FromQuery] int? limit, CancellationToken ct)
    {
        var n = limit ?? 50;
        if (n is < 1 or > MaxFailed)
        {
            throw new ValidationException([new ValidationFailure("Limit", $"deve estar entre 1 e {MaxFailed}")]);
        }
        return new FailedSupplierOrderList([.. (await actions.ListFailedAsync(n, ct)).Select(FailedSupplierOrder.From)]);
    }

    /// <summary>retrySupplierOrder: tenta agora o pedido que espera a próxima tentativa.</summary>
    [HttpPost("v1/admin/supplier-orders/{order_id:guid}/retry")]
    public async Task<IActionResult> Retry([FromRoute(Name = "order_id")] Guid orderId, CancellationToken ct) =>
        Result(await actions.RetryAsync(orderId, ct), "O pedido não está esperando uma nova tentativa");

    /// <summary>cancelSupplierOrder: cancela o que não foi despachado; o cliente é estornado.</summary>
    [HttpPost("v1/admin/supplier-orders/{order_id:guid}/cancel")]
    public async Task<IActionResult> Cancel([FromRoute(Name = "order_id")] Guid orderId, CancellationToken ct)
    {
        try
        {
            return Result(await actions.CancelAsync(orderId, ct), "O pedido já foi despachado ou está sendo enviado ao fornecedor");
        }
        catch (SupplierUnavailableException e)
        {
            throw new DependencyUnavailableException($"o fornecedor não respondeu: {e.Message}", TimeSpan.FromSeconds(30));
        }
    }

    private NoContentResult Result(SupplierOrderActionResult result, string notAllowed) => result switch
    {
        SupplierOrderActionResult.Done => NoContent(),
        SupplierOrderActionResult.NotFound => throw new NotFoundException("pedido não encontrado no fornecedor"),
        _ => throw new ConflictException("Ação indisponível", notAllowed),
    };

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

    /// <summary>
    /// instructSupplierReturnCustomer: depois da recusa do fornecedor na abertura, o que a loja combinou com o
    /// cliente (#281). O cliente recebe por e-mail.
    /// </summary>
    [HttpPost("v1/admin/supplier-returns/{order_id:guid}/customer-instruction")]
    public async Task<IActionResult> InstructCustomer(
        [FromRoute(Name = "order_id")] Guid orderId, [FromBody] CustomerInstructionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var failures = request.Validate();
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
        return await returnActions.InstructCustomerAsync(orderId, request.Instruction!.Value, request.ReturnCode, request.ReturnAddress, ct) switch
        {
            SupplierReturnActionResult.Done => NoContent(),
            SupplierReturnActionResult.NotFound => throw new NotFoundException("devolução não encontrada"),
            _ => throw new ConflictException("Ação indisponível",
                "A devolução não foi recusada na abertura, ou o cliente já recebeu outra instrução"),
        };
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

public sealed record OrderMarginItem(
    Guid OrderId,
    OrderOutcome Outcome,
    long? RevenueCents,
    long? ProductCostCents,
    long? FreightCents,
    long? FeeCents,
    long? MarginCents,
    string CustomerEmail,
    DateTimeOffset CreatedAt)
{
    public static OrderMarginItem From(OrderMargin m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return new(m.Order.Id, m.Outcome, m.RevenueCents, m.ProductCostCents, m.FreightCents, m.FeeCents, m.MarginCents, m.Order.CustomerEmail, m.Order.CreatedAt);
    }
}

public sealed record OrderMarginList(IReadOnlyList<OrderMarginItem> Data);

public sealed record FailedSupplierOrder(
    Guid OrderId,
    string Reason,
    string? Detail,
    string Supplier,
    string? SupplierOrderId,
    string CustomerEmail,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset FailedAt)
{
    public static FailedSupplierOrder From(SupplierOrder o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new(o.Id, o.FailureReason ?? "", o.FailureDetail, o.Supplier, o.SupplierOrderId, o.CustomerEmail, o.Attempts, o.CreatedAt, o.FailedAt!.Value);
    }
}

public sealed record FailedSupplierOrderList(IReadOnlyList<FailedSupplierOrder> Data);

/// <summary>O corpo do instructSupplierReturnCustomer: no envio, o código e o endereço da loja.</summary>
public sealed record CustomerInstructionRequest(CustomerReturnInstruction? Instruction, string? ReturnCode, string? ReturnAddress)
{
    public List<ValidationFailure> Validate()
    {
        List<ValidationFailure> failures = [];
        if (Instruction is null)
        {
            failures.Add(new(nameof(Instruction), "escolha keep ou send"));
        }
        else if (Instruction == CustomerReturnInstruction.Send)
        {
            if (string.IsNullOrWhiteSpace(ReturnCode) || ReturnCode.Trim().Length > 64)
            {
                failures.Add(new(nameof(ReturnCode), "o envio precisa do código da devolução, com até 64 caracteres"));
            }
            if (string.IsNullOrWhiteSpace(ReturnAddress) || ReturnAddress.Trim().Length > 500)
            {
                failures.Add(new(nameof(ReturnAddress), "o envio precisa do endereço, com até 500 caracteres"));
            }
        }
        else if (ReturnCode is not null || ReturnAddress is not null)
        {
            failures.Add(new(nameof(Instruction), "quem fica com o produto não recebe código nem endereço"));
        }
        return failures;
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
    string? ReturnAddress,
    string CustomerEmail,
    IReadOnlyList<SupplierOrderItem> Items,
    int Attempts,
    string? LastError,
    string? ResolutionReason,
    string? InvoiceError,
    DateTimeOffset RequestedAt,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? ResolvedAt,
    bool AwaitingCustomerInstruction,
    CustomerReturnInstruction? CustomerInstruction,
    DateTimeOffset? InstructedAt)
{
    public static SupplierReturnItem From(PendingSupplierReturn pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var r = pending.Return;
        return new(r.Id, r.Status, pending.Overdue, pending.DueAt, r.Supplier, r.SupplierOrderId, r.DisputeId, r.ReturnCode, r.ReturnAddress,
            r.CustomerEmail, r.Items, r.Attempts, r.LastError, r.ResolutionReason, pending.InvoiceError, r.RequestedAt, r.OpenedAt, r.ResolvedAt,
            r.AwaitingCustomerInstruction, r.CustomerInstruction, r.InstructedAt);
    }
}

public sealed record SupplierReturnList(IReadOnlyList<SupplierReturnItem> Data);

public sealed record OverdueSupplierOrderList(IReadOnlyList<OverdueSupplierOrder> Data);
