using Embrulhado.BuildingBlocks.Web;
using Embrulhado.Fulfillment.Application.ManualSupplier;
using Embrulhado.Fulfillment.Application.SupplierOrders;
using Embrulhado.Fulfillment.Domain;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Embrulhado.Fulfillment.Api.Controllers;

/// <summary>
/// O fornecedor manual (#412) pelo painel (contracts/openapi/fulfillment.v1.yaml): a fila "Pedidos a fazer" e o que a
/// equipe registra no lugar da API do fornecedor. A fila é também do auditor (o Balcão do agentes-hub), sem os dados
/// de entrega do cliente; as ações são do operador.
/// </summary>
[ApiController]
[Produces("application/json")]
[Authorize(Policy = JwtAuth.AdminReadPolicy)]
public sealed class AdminManualOrdersController(ManualOrderActions actions) : ControllerBase
{
    /// <summary>listSupplierOrdersToPlace: os pedidos pagos esperando a equipe, os mais antigos primeiro.</summary>
    [HttpGet("v1/admin/supplier-orders/to-place")]
    public async Task<ToPlaceList> ToPlace([FromQuery] int? limit, CancellationToken ct)
    {
        var n = limit ?? 50;
        if (n is < 1 or > ManualOrderActions.MaxToPlace)
        {
            throw new ValidationException([new ValidationFailure("Limit", $"deve estar entre 1 e {ManualOrderActions.MaxToPlace}")]);
        }
        var operatorView = User.IsInRole("admin");
        return new ToPlaceList([.. (await actions.ListToPlaceAsync(n, ct)).Select(o => ToPlaceOrder.From(o, operatorView))]);
    }

    /// <summary>listManualSupplierOrders: os do fornecedor manual feitos (esperando o rastreio) ou em transporte.</summary>
    [HttpGet("v1/admin/supplier-orders/manual")]
    public async Task<ToPlaceList> InProgress([FromQuery] SupplierOrderStatus? status, [FromQuery] int? limit, CancellationToken ct)
    {
        var n = limit ?? 50;
        List<ValidationFailure> failures = [];
        if (n is < 1 or > ManualOrderActions.MaxToPlace)
        {
            failures.Add(new("Limit", $"deve estar entre 1 e {ManualOrderActions.MaxToPlace}"));
        }
        if (status is not (SupplierOrderStatus.Placed or SupplierOrderStatus.Shipped))
        {
            failures.Add(new("Status", "placed (esperando o rastreio) ou shipped (em transporte)"));
        }
        Validate(failures);
        var operatorView = User.IsInRole("admin");
        return new ToPlaceList([.. (await actions.ListInProgressAsync(status!.Value, n, ct)).Select(o => ToPlaceOrder.From(o, operatorView))]);
    }

    /// <summary>markSupplierOrderPlaced: a equipe fez o pedido no fornecedor. O cliente vê "enviado ao fornecedor".</summary>
    [HttpPost("v1/admin/supplier-orders/{order_id:guid}/placed")]
    [Authorize(Policy = JwtAuth.AdminPolicy)]
    public async Task<IActionResult> Placed([FromRoute(Name = "order_id")] Guid orderId, [FromBody] PlacedRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request.Validate());
        var by = User.FindFirst("email")?.Value ?? User.FindFirst("sub")?.Value ?? "equipe";
        return Result(await actions.PlacedAsync(orderId,
            new StaffPlacement(request.SupplierOrderId!, request.PaidProductCostCents, request.PaidFreightCostCents, by), ct),
            "O pedido não está na fila de pedidos a fazer");
    }

    /// <summary>
    /// correctSupplierOrderPlacement (#480): corrige o número do pedido no fornecedor e o que a equipe pagou, enquanto
    /// o pedido está feito ou em transporte. A margem passa a usar os valores novos; o antes e o depois ficam guardados.
    /// </summary>
    [HttpPut("v1/admin/supplier-orders/{order_id:guid}/placement")]
    [Authorize(Policy = JwtAuth.AdminPolicy)]
    public async Task<IActionResult> CorrectPlacement([FromRoute(Name = "order_id")] Guid orderId, [FromBody] PlacedRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request.Validate());
        var by = User.FindFirst("email")?.Value ?? User.FindFirst("sub")?.Value ?? "equipe";
        return Result(await actions.CorrectPlacementAsync(orderId,
            new StaffPlacement(request.SupplierOrderId!, request.PaidProductCostCents, request.PaidFreightCostCents, by), ct),
            "Só dá para corrigir o pedido feito no fornecedor ou em transporte");
    }

    /// <summary>markSupplierOrderOutOfStock: o fornecedor não tinha o produto. O pedido falha, e o cliente é estornado.</summary>
    [HttpPost("v1/admin/supplier-orders/{order_id:guid}/out-of-stock")]
    [Authorize(Policy = JwtAuth.AdminPolicy)]
    public async Task<IActionResult> OutOfStock(
        [FromRoute(Name = "order_id")] Guid orderId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] OutOfStockRequest? request,
        CancellationToken ct)
    {
        if (request?.Detail is { Length: > 500 })
        {
            throw new ValidationException([new ValidationFailure("Detail", "até 500 caracteres")]);
        }
        return Result(await actions.OutOfStockAsync(orderId, request?.Detail, ct), "O pedido não está na fila de pedidos a fazer");
    }

    /// <summary>markSupplierOrderShipped: o rastreio do fornecedor. O cliente recebe o código.</summary>
    [HttpPost("v1/admin/supplier-orders/{order_id:guid}/tracking")]
    [Authorize(Policy = JwtAuth.AdminPolicy)]
    public async Task<IActionResult> Tracking([FromRoute(Name = "order_id")] Guid orderId, [FromBody] TrackingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request.Validate());
        return Result(await actions.ShippedAsync(orderId, new Tracking(request.Number!.Trim(), request.Carrier?.Trim(), request.Url?.Trim()), ct),
            "O pedido ainda não foi feito no fornecedor, ou já foi despachado");
    }

    /// <summary>markSupplierOrderDelivered: o rastreio mostra a entrega.</summary>
    [HttpPost("v1/admin/supplier-orders/{order_id:guid}/delivered")]
    [Authorize(Policy = JwtAuth.AdminPolicy)]
    public async Task<IActionResult> Delivered([FromRoute(Name = "order_id")] Guid orderId, CancellationToken ct) =>
        Result(await actions.DeliveredAsync(orderId, ct), "O pedido não está em transporte");

    private static void Validate(List<ValidationFailure> failures)
    {
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    private NoContentResult Result(SupplierOrderActionResult result, string notAllowed) => result switch
    {
        SupplierOrderActionResult.Done => NoContent(),
        SupplierOrderActionResult.NotFound => throw new NotFoundException("pedido do fornecedor manual não encontrado"),
        _ => throw new ConflictException("Ação indisponível", notAllowed),
    };
}

public sealed record PlacedRequest(string? SupplierOrderId, long? PaidProductCostCents, long? PaidFreightCostCents)
{
    public List<ValidationFailure> Validate()
    {
        List<ValidationFailure> failures = [];
        if (string.IsNullOrWhiteSpace(SupplierOrderId) || SupplierOrderId.Trim().Length > 100)
        {
            failures.Add(new(nameof(SupplierOrderId), "o número do pedido no fornecedor, com até 100 caracteres"));
        }
        if (PaidProductCostCents is <= 0)
        {
            failures.Add(new(nameof(PaidProductCostCents), "deve ser maior que zero"));
        }
        if (PaidFreightCostCents is < 0)
        {
            failures.Add(new(nameof(PaidFreightCostCents), "não pode ser negativo"));
        }
        return failures;
    }
}

public sealed record OutOfStockRequest(string? Detail);

public sealed record TrackingRequest(string? Number, string? Carrier, string? Url)
{
    public List<ValidationFailure> Validate()
    {
        List<ValidationFailure> failures = [];
        if (string.IsNullOrWhiteSpace(Number) || Number.Trim().Length > 64)
        {
            failures.Add(new(nameof(Number), "o código de rastreio, com até 64 caracteres"));
        }
        if (Carrier is { Length: > 64 })
        {
            failures.Add(new(nameof(Carrier), "até 64 caracteres"));
        }
        if (Url is not null && (!Uri.TryCreate(Url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || Url.Length > 500))
        {
            failures.Add(new(nameof(Url), "um endereço https://, com até 500 caracteres"));
        }
        return failures;
    }
}

/// <summary>Os dados de entrega, só para o operador: é com eles que a equipe preenche o pedido no fornecedor.</summary>
public sealed record ToPlaceShipTo(
    string Name, string Cpf, string Phone, string ZipCode, string Street, string Number, string? Complement, string District, string City, string State)
{
    public static ToPlaceShipTo From(ShipTo s) => new(s.Name, s.Cpf, s.Phone, s.ZipCode, s.Street, s.Number, s.Complement, s.District, s.City, s.State);
}

public sealed record ToPlaceOrder(
    Guid OrderId,
    DateTimeOffset AwaitingSince,
    DateOnly? PromisedBy,
    IReadOnlyList<SupplierOrderItem> Items,
    long? RevenueCents,
    long? FreightChargedCents,
    string? ShippingService,
    long? ExpectedProductCostCents,
    long? ExpectedFreightCostCents,
    string? CustomerEmail,
    ToPlaceShipTo? ShipTo,
    SupplierOrderStatus Status,
    string? SupplierOrderId,
    long? PaidProductCostCents,
    long? PaidFreightCostCents,
    Tracking? Tracking,
    DateTimeOffset? PlacedAt,
    DateTimeOffset? ShippedAt)
{
    public static ToPlaceOrder From(SupplierOrder o, bool operatorView)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new(o.Id, o.AwaitingStaffSince ?? o.CreatedAt, o.PromisedBy, o.Items, o.RevenueCents, o.FreightCents, o.ShippingService,
            o.ProductCostCents, o.FreightCostCents,
            operatorView ? o.CustomerEmail : null, operatorView && o.ShipTo is { } s ? ToPlaceShipTo.From(s) : null,
            o.Status, o.SupplierOrderId, o.PaidProductCostCents, o.PaidFreightCostCents, o.Tracking, o.PlacedAt, o.ShippedAt);
    }
}

public sealed record ToPlaceList(IReadOnlyList<ToPlaceOrder> Data);
