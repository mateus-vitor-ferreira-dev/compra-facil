using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Orders.Application.Orders;
using CompraFacil.Orders.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Orders.Api.Controllers;

/// <summary>O contato de quem comprou, para atender: sem CPF nem endereço (LGPD, só o necessário).</summary>
public sealed record AdminCustomerResponse(string Email, string? Name, string? Phone);

public sealed record AdminOrderDestinationResponse(string City, string State);

public sealed record AdminOrderPaymentResponse(PaymentMethod Method, int Installments);

public sealed record AdminOrderSummaryResponse(
    Guid Id, OrderStatus Status, CancellationReason? CancellationReason, DateTimeOffset CreatedAt, Money Total, int ItemsCount,
    AdminOrderPaymentResponse Payment, AdminCustomerResponse Customer, AdminOrderDestinationResponse? Destination)
{
    public static AdminOrderSummaryResponse From(Order o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new(o.Id, o.Status, o.CancellationReason, o.CreatedAt, o.Total, o.Items.Sum(i => i.Quantity),
            new AdminOrderPaymentResponse(o.PaymentMethod, o.Installments),
            new AdminCustomerResponse(o.CustomerEmail, o.Recipient?.Name, o.Recipient?.Phone),
            o.ShippingAddress is { } a ? new AdminOrderDestinationResponse(a.City, a.State) : null);
    }
}

public sealed record AdminOrderDetailResponse(
    Guid Id, OrderStatus Status, CancellationReason? CancellationReason, DateTimeOffset CreatedAt, Money Total, int ItemsCount,
    AdminOrderPaymentResponse Payment, AdminCustomerResponse Customer, AdminOrderDestinationResponse? Destination,
    IReadOnlyList<OrderItemData> Items, Money ItemsTotal, OrderShippingResponse? Shipping, Tracking? Tracking,
    string? PaymentFailureReason, int? InvoiceNumber, DateTimeOffset? StockReservedAt, DateTimeOffset? SentToSupplierAt,
    DateTimeOffset? ShippedAt, DateTimeOffset? DeliveredAt, DateTimeOffset? ReturnRequestedAt, DateTimeOffset? RefundedAt,
    DateTimeOffset? RefundFailedAt, DateTimeOffset UpdatedAt)
{
    public static AdminOrderDetailResponse From(Order o)
    {
        var s = AdminOrderSummaryResponse.From(o);
        return new(s.Id, s.Status, s.CancellationReason, s.CreatedAt, s.Total, s.ItemsCount, s.Payment, s.Customer, s.Destination,
            [.. o.Items.Select(OrderItemData.From)], o.Items.Aggregate(Money.Zero(o.Total.Currency), (sum, i) => sum.Plus(i.LineTotal)),
            OrderShippingResponse.From(o.Shipping), o.Tracking, o.PaymentFailureReason, o.Invoice?.Number, o.StockReservedAt,
            o.SentToSupplierAt, o.ShippedAt, o.DeliveredAt, o.ReturnRequestedAt, o.RefundedAt, o.RefundFailedAt, o.UpdatedAt);
    }
}

public sealed record AdminOrderListResponse(IReadOnlyList<AdminOrderSummaryResponse> Data, PageInfo Page);

public sealed record SalesCountsResponse(int Placed, int Paid, int Refunded, int NotPaid);

public sealed record DailySalesResponse(DateOnly Date, int PaidOrders, Money GrossSales, Money Refunds);

public sealed record MethodSalesResponse(PaymentMethod Method, int PaidOrders, Money GrossSales);

public sealed record ProductSalesResponse(Guid ProductId, string Name, int Quantity, Money Revenue);

public sealed record SalesSummaryResponse(
    DateTimeOffset From, DateTimeOffset To, SalesCountsResponse Orders, Money GrossSales, Money Refunds, Money NetSales,
    Money AverageTicket, Money ShippingCharged, IReadOnlyList<DailySalesResponse> Daily, IReadOnlyList<MethodSalesResponse> ByPaymentMethod,
    IReadOnlyList<ProductSalesResponse> TopProducts)
{
    public static SalesSummaryResponse Of(SalesSummary s)
    {
        ArgumentNullException.ThrowIfNull(s);
        Money M(long cents) => new(cents, s.Currency);
        return new(s.Period.From, s.Period.To, new SalesCountsResponse(s.Placed, s.Paid, s.Refunded, s.NotPaid),
            M(s.GrossCents), M(s.RefundCents), M(s.NetCents), M(s.AverageTicketCents), M(s.ShippingChargedCents),
            [.. s.Daily.Select(d => new DailySalesResponse(d.Date, d.PaidOrders, M(d.GrossCents), M(d.RefundCents)))],
            [.. s.ByMethod.Select(m => new MethodSalesResponse(m.Method, m.PaidOrders, M(m.GrossCents)))],
            [.. s.TopProducts.Select(p => new ProductSalesResponse(p.ProductId, p.Name, p.Quantity, M(p.RevenueCents)))]);
    }
}

/// <summary>A operação dos pedidos (papel <c>admin</c>): a lista de todos, o detalhe e o resumo de vendas.</summary>
[ApiController]
[Produces("application/json")]
[Authorize(Policy = JwtAuth.AdminReadPolicy)] // só leitura: lista, detalhe e resumo (#403)
public sealed class AdminOrdersController(AdminOrders admin) : ControllerBase
{
    /// <summary>listAdminOrders: do mais recente para o mais antigo, com filtro, busca e período.</summary>
    [HttpGet("v1/admin/orders")]
    public async Task<AdminOrderListResponse> List(
        [FromQuery(Name = "status")] OrderStatus? status,
        [FromQuery(Name = "q")] string? search,
        [FromQuery(Name = "from")] DateTimeOffset? from,
        [FromQuery(Name = "to")] DateTimeOffset? to,
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        CancellationToken ct)
    {
        var page = await admin.ListAsync(
            new AdminOrdersQuery(new AdminOrderFilter(status, search, from, to), cursor, limit ?? AdminOrdersQuery.DefaultLimit), ct);
        return new AdminOrderListResponse(
            [.. page.Items.Select(AdminOrderSummaryResponse.From)], new PageInfo(page.NextCursor, HasMore: page.NextCursor is not null));
    }

    /// <summary>getAdminOrder: o pedido com itens, entrega, rastreio e as datas de cada etapa.</summary>
    [HttpGet("v1/admin/orders/{order_id:guid}")]
    public async Task<AdminOrderDetailResponse> Get([FromRoute(Name = "order_id")] Guid orderId, CancellationToken ct) =>
        AdminOrderDetailResponse.From(await admin.GetAsync(orderId, ct));

    /// <summary>getSalesSummary: pagos, estornados, por dia, por meio de pagamento e os mais vendidos.</summary>
    [HttpGet("v1/admin/sales-summary")]
    public async Task<SalesSummaryResponse> Summary([FromQuery(Name = "from")] DateTimeOffset? from, [FromQuery(Name = "to")] DateTimeOffset? to,
        CancellationToken ct)
    {
        if (from is not { } start || to is not { } end)
        {
            throw new FluentValidation.ValidationException([
                new FluentValidation.Results.ValidationFailure(from is null ? "from" : "to", "obrigatório")]);
        }
        return SalesSummaryResponse.Of(await admin.SummaryAsync(new SalesPeriod(start, end), ct));
    }
}
