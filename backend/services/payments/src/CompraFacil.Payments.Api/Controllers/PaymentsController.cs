using CompraFacil.Payments.Application.Payments;
using CompraFacil.Payments.Domain;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Payments.Api.Controllers;

/// <summary>
/// Consulta de pagamentos (contracts/openapi/payments.v1.yaml). A cobrança não começa por HTTP: vem
/// da SAGA (OrderPlaced + StockReserved).
/// </summary>
[ApiController]
[Route("v1/payments")]
[Produces("application/json")]
public sealed class PaymentsController(PaymentQueries queries) : ControllerBase
{
    /// <summary>listPayments: pagamentos de um pedido.</summary>
    [HttpGet]
    public async Task<PaymentListResponse> List([FromQuery(Name = "order_id")] Guid? orderId, CancellationToken ct)
    {
        if (orderId is null || orderId == Guid.Empty)
        {
            throw new ValidationException([new ValidationFailure("OrderId", "obrigatório")]);
        }
        return new PaymentListResponse([.. (await queries.ListByOrderAsync(orderId.Value, ct)).Select(PaymentResponse.From)]);
    }

    /// <summary>getPayment: detalhe de um pagamento.</summary>
    [HttpGet("{payment_id:guid}")]
    public async Task<PaymentResponse> Get([FromRoute(Name = "payment_id")] Guid paymentId, CancellationToken ct) =>
        PaymentResponse.From(await queries.GetAsync(paymentId, ct));
}

public sealed record PixResponse(string QrCode, string QrCodeBase64, string? TicketUrl, DateTimeOffset ExpiresAt);

public sealed record PaymentResponse(
    Guid Id, Guid OrderId, Money Amount, PaymentStatus Status, FailureReason? FailureReason, PaymentMethod Method, int Installments,
    string Provider, string? ProviderReference, string? CardLastFour, PixResponse? Pix, long FeeCents, long RefundedCents,
    DateTimeOffset? CapturedAt, DateTimeOffset? RefundedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static PaymentResponse From(Payment p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new(p.Id, p.OrderId, p.Amount, p.Status, p.FailureReason, p.Method, p.Installments, p.Provider, p.ProviderReference,
            p.CardLastFour, p.Pix is { } x ? new PixResponse(x.QrCode, x.QrCodeBase64, x.TicketUrl, x.ExpiresAt) : null,
            p.FeeCents, p.RefundedCents, p.CapturedAt, p.RefundedAt, p.CreatedAt, p.UpdatedAt);
    }
}

public sealed record PaymentListResponse(IReadOnlyList<PaymentResponse> Data);
