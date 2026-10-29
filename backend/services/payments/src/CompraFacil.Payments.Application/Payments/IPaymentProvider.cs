using CompraFacil.Payments.Domain;

namespace CompraFacil.Payments.Application.Payments;

/// <summary>O que o provedor diz de uma cobrança (o status dele, traduzido).</summary>
public enum ProviderStatus
{
    /// <summary>PIX esperando o pagamento.</summary>
    Pending,

    /// <summary>Cartão em análise.</summary>
    InProcess,

    Authorized,
    Approved,
    Rejected,

    /// <summary>Autorização ou PIX cancelado (por nós).</summary>
    Cancelled,

    /// <summary>PIX que passou da validade sem pagamento.</summary>
    Expired,

    Refunded,
}

/// <summary>Uma cobrança como o provedor a devolve.</summary>
public sealed record ProviderPayment(
    string Reference, ProviderStatus Status, FailureReason? FailureReason = null, PixCharge? Pix = null, string? CardLastFour = null);

public sealed record ProviderRefund(string Reference, long AmountCents);

/// <summary>PIX a gerar. A chave de idempotência é a do pedido: repetir não gera outro QR.</summary>
public sealed record PixRequest(string IdempotencyKey, Guid OrderId, Money Amount, string? PayerEmail, DateTimeOffset ExpiresAt);

/// <summary>Cartão a autorizar (sem capturar): a captura vem quando o fornecedor aceita o pedido.</summary>
public sealed record CardRequest(string IdempotencyKey, Guid OrderId, Money Amount, string? PayerEmail, string CardToken, string CardBrand, int Installments);

/// <summary>O provedor recusou o pedido em si (4xx: token usado ou inválido, estado que não permite).</summary>
public sealed class PaymentProviderRejectedException(string message) : Exception(message);

/// <summary>Provedor sem resposta (timeout, 5xx): não é recusa, a operação é tentada de novo.</summary>
public sealed class PaymentProviderUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// O gateway de pagamento (ADR 0007, #220): o adaptador do Mercado Pago, que em dev e no CI fala com o
/// simulado (backend/services/payment-sim). Toda operação que cobra ou devolve é idempotente pela chave:
/// se o payments cair depois de chamar e antes de gravar, a nova tentativa devolve o mesmo resultado.
/// </summary>
public interface IPaymentProvider
{
    string Name { get; }

    Task<ProviderPayment> CreatePixAsync(PixRequest request, CancellationToken ct);

    Task<ProviderPayment> AuthorizeCardAsync(CardRequest request, CancellationToken ct);

    Task<ProviderPayment> GetAsync(string reference, CancellationToken ct);

    Task<ProviderPayment> CaptureAsync(string reference, CancellationToken ct);

    /// <summary>Cancela a autorização ou o PIX não pago.</summary>
    Task<ProviderPayment> CancelAsync(string reference, CancellationToken ct);

    /// <summary>Devolve <paramref name="amountCents"/> (nulo: o que falta).</summary>
    Task<ProviderRefund> RefundAsync(string reference, long? amountCents, string idempotencyKey, CancellationToken ct);

    /// <summary>Confere a assinatura de uma notificação do provedor.</summary>
    bool VerifyNotification(string dataId, string? requestId, string? signature);
}
