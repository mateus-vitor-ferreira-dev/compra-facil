using CompraFacil.Orders.Application.Orders;
using CompraFacil.Orders.Domain;

namespace CompraFacil.Orders.Api.Controllers;

// Corpos HTTP do contrato (contracts/openapi/orders.v1.yaml). Campos opcionais aqui para a
// ausência virar 422 do validador (com o nome do campo), e não um 400 genérico de binding.

// o cliente e o e-mail vêm do token; customer_id/customer_email de clientes antigos são ignorados
public sealed record PlaceOrderRequest(
    IReadOnlyList<PlaceOrderItemRequest>? Items, PlaceOrderPaymentRequest? Payment, RecipientRequest? Recipient, ShippingAddressRequest? ShippingAddress,
    ShippingChoiceRequest? Shipping);

/// <summary>O frete escolhido (#222): só o serviço; o preço e o prazo o orders recota no fulfillment.</summary>
public sealed record ShippingChoiceRequest(string? Service);

/// <summary>O frete do pedido como o cliente vê: o serviço, o preço, o prazo em dias úteis e a data prometida.</summary>
public sealed record OrderShippingResponse(string Service, string Name, Money Price, int MinDays, int MaxDays, DateOnly PromisedBy)
{
    public static OrderShippingResponse? From(OrderShipping? s) =>
        s is null ? null : new(s.Service, s.Name, s.Price, s.MinDays, s.MaxDays, s.PromisedBy);
}

/// <summary>Quem recebe (#221): o CPF e o telefone podem vir com máscara; ficam só os dígitos.</summary>
public sealed record RecipientRequest(string? Name, string? Cpf, string? Phone)
{
    public Recipient ToDomain() => new(Clean(Name), BrazilianDocuments.DigitsOf(Cpf), BrazilianDocuments.DigitsOf(Phone));

    internal static string Clean(string? value) => string.Join(' ', (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Onde entregar: o CEP pode vir com hífen; a UF, em minúsculas.</summary>
public sealed record ShippingAddressRequest(
    string? ZipCode, string? Street, string? Number, string? Complement, string? District, string? City, string? State)
{
    public ShippingAddress ToDomain() => new(
        BrazilianDocuments.DigitsOf(ZipCode), RecipientRequest.Clean(Street), RecipientRequest.Clean(Number),
        string.IsNullOrWhiteSpace(Complement) ? null : RecipientRequest.Clean(Complement),
        RecipientRequest.Clean(District), RecipientRequest.Clean(City), RecipientRequest.Clean(State).ToUpperInvariant());
}

/// <summary>Como pagar: pix, ou card com o card_token do provedor (tokenizado no checkout), a bandeira e as parcelas.</summary>
public sealed record PlaceOrderPaymentRequest(PaymentMethod? Method, string? CardToken, string? CardBrand, int? Installments);

public sealed record OrderPaymentResponse(PaymentMethod Method, int Installments, OrderPix? Pix);

public sealed record PlaceOrderItemRequest(Guid? ProductId, int? Quantity, long? ExpectedUnitPriceCents);

public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    OrderStatus Status,
    CancellationReason? CancellationReason,
    IReadOnlyList<OrderItemData> Items,
    Money ItemsTotal,
    OrderShippingResponse? Shipping,
    Money Total,
    Guid? PaymentId,
    OrderPaymentResponse Payment,
    Recipient? Recipient,
    ShippingAddress? ShippingAddress,
    OrderInvoice? Invoice,
    DateTimeOffset? StockReservedAt,
    DateTimeOffset? SentToSupplierAt,
    Tracking? Tracking,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? RefundedAt,
    DateTimeOffset? ReturnRequestedAt,
    DateTimeOffset? ReturnableUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static OrderResponse From(Order o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new(o.Id, o.CustomerId, o.Status, o.CancellationReason, [.. o.Items.Select(OrderItemData.From)],
            o.Items.Aggregate(Money.Zero(o.Total.Currency), (sum, i) => sum.Plus(i.LineTotal)), OrderShippingResponse.From(o.Shipping),
            o.Total, o.PaymentId, new OrderPaymentResponse(o.PaymentMethod, o.Installments, o.Pix), o.Recipient, o.ShippingAddress, o.Invoice, o.StockReservedAt, o.SentToSupplierAt, o.Tracking, o.ShippedAt, o.DeliveredAt, o.RefundedAt, o.ReturnRequestedAt, o.ReturnableUntil,
            o.CreatedAt, o.UpdatedAt);
    }
}

public sealed record PageInfo(string? NextCursor, bool HasMore);

public sealed record OrderListResponse(IReadOnlyList<OrderResponse> Data, PageInfo Page);
