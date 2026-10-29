using CompraFacil.Payments.Domain;

namespace CompraFacil.Payments.Application.Payments;

/// <summary>Cobrança a fazer no gateway.</summary>
/// <param name="IdempotencyKey">Mesma chave = mesma cobrança: o gateway não cobra duas vezes (usamos o order_id).</param>
/// <param name="OrderId">Pedido cobrado.</param>
/// <param name="CustomerId">Cliente do pedido.</param>
/// <param name="Amount">Total do pedido, congelado na criação.</param>
public sealed record ChargeRequest(string IdempotencyKey, Guid OrderId, Guid CustomerId, Money Amount);

/// <summary>Resposta definitiva do gateway.</summary>
public abstract record ChargeResult;

public sealed record ChargeApproved(string ProviderReference) : ChargeResult;

public sealed record ChargeDeclined(FailureReason Reason, string? ProviderReference) : ChargeResult;

/// <summary>Estorno do valor todo de uma cobrança aprovada.</summary>
/// <param name="IdempotencyKey">Mesma chave = mesmo estorno (usamos o payment_id).</param>
/// <param name="ProviderReference">A cobrança no gateway.</param>
/// <param name="Amount">Valor cobrado, devolvido inteiro.</param>
public sealed record RefundRequest(string IdempotencyKey, string ProviderReference, Money Amount);

/// <summary>Gateway sem resposta (timeout, 5xx): não é recusa, a cobrança é tentada de novo.</summary>
public sealed class PaymentGatewayUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Gateway de pagamento (fake em dev; Stripe em modo teste é opcional). Tem que ser idempotente pela
/// <see cref="ChargeRequest.IdempotencyKey"/>: se o payments cair depois de cobrar e antes de gravar,
/// a nova tentativa devolve a mesma cobrança em vez de cobrar de novo.
/// </summary>
public interface IPaymentGateway
{
    string Provider { get; }

    /// <exception cref="PaymentGatewayUnavailableException">Falha temporária; tentar de novo.</exception>
    Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct);

    /// <summary>Devolve a cobrança ao cliente e diz o ID do estorno; idempotente pela chave.</summary>
    /// <exception cref="PaymentGatewayUnavailableException">Falha temporária; tentar de novo.</exception>
    Task<string> RefundAsync(RefundRequest request, CancellationToken ct);
}
