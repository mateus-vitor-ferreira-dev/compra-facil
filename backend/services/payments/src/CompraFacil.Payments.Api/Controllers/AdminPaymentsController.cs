using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Payments.Application.Payments;
using CompraFacil.Payments.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Payments.Api.Controllers;

/// <summary>refundPayment (contracts/openapi/payments.v1.yaml): estorno de parte ou do resto, pelo painel.</summary>
[ApiController]
[Produces("application/json")]
[Authorize(Policy = JwtAuth.AdminPolicy)]
public sealed class AdminPaymentsController(RefundPayment refund) : ControllerBase
{
    [HttpPost("v1/admin/payments/{payment_id:guid}/refunds")]
    public async Task<PaymentResponse> Refund(
        [FromRoute(Name = "payment_id")] Guid paymentId, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        RefundRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
        {
            throw new FluentValidation.ValidationException([new FluentValidation.Results.ValidationFailure("Idempotency-Key", "obrigatório (até 128)")]);
        }
        try
        {
            return PaymentResponse.From(await refund.HandleAsync(paymentId, body.AmountCents, idempotencyKey, ct));
        }
        catch (DomainException e)
        {
            throw new ConflictException("Estorno recusado", e.Message);
        }
        catch (PaymentProviderUnavailableException e)
        {
            throw new DependencyUnavailableException($"o provedor não respondeu: {e.Message}", TimeSpan.FromSeconds(10));
        }
    }
}

/// <summary>AmountCents nulo: estorna o que falta.</summary>
public sealed record RefundRequest(long? AmountCents);
