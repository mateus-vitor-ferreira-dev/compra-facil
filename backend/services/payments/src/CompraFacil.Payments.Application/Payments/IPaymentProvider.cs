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

    /// <summary>
    /// Pago, mas o cliente contestou: reclamação aberta (in_mediation) ou chargeback no cartão (charged_back). Não
    /// muda o pedido sozinho; a equipe acompanha no painel do provedor.
    /// </summary>
    Disputed,
}

/// <summary>Uma cobrança como o provedor a devolve.</summary>
public sealed record ProviderPayment(
    string Reference, ProviderStatus Status, FailureReason? FailureReason = null, PixCharge? Pix = null, string? CardLastFour = null);

public sealed record ProviderRefund(string Reference, long AmountCents);

/// <summary>PIX a gerar. Repetir para o mesmo pedido não gera outro QR (o txid sai do pedido).</summary>
public sealed record PixRequest(
    string IdempotencyKey, Guid OrderId, Money Amount, string? PayerEmail, DateTimeOffset ExpiresAt, Payer? Payer = null);

/// <summary>Cartão a cobrar, pelo token que o navegador gerou no provedor.</summary>
public sealed record CardRequest(
    string IdempotencyKey, Guid OrderId, Money Amount, string? PayerEmail, string CardToken, string CardBrand, int Installments, Payer? Payer = null);

/// <summary>O provedor recusou o pedido em si (4xx: token usado ou inválido, estado que não permite).</summary>
public sealed class PaymentProviderRejectedException(string message) : Exception(message);

/// <summary>Provedor sem resposta (timeout, 5xx): não é recusa, a operação é tentada de novo.</summary>
public sealed class PaymentProviderUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// O gateway de pagamento (ADR 0007, #379): o adaptador da Efí, que em dev e no CI fala com o simulado
/// (backend/services/payment-sim). Nada aqui cobra duas vezes numa nova tentativa: o PIX tem o txid do pedido, e o
/// cartão é cobrado em dois passos (abrir a cobrança, gravar a referência, pagar), porque a Efí não tem chave de
/// idempotência e pagar uma cobrança que já tem forma de pagamento é recusado.
/// </summary>
public interface IPaymentProvider
{
    string Name { get; }

    Task<ProviderPayment> CreatePixAsync(PixRequest request, CancellationToken ct);

    /// <summary>Abre a cobrança do cartão, sem forma de pagamento (nada é cobrado). Devolve a referência.</summary>
    Task<string> OpenCardChargeAsync(CardRequest request, CancellationToken ct);

    /// <summary>
    /// Paga a cobrança aberta com o cartão. Se ela já foi paga (a tentativa repetida), devolve como está, sem
    /// cobrar de novo.
    /// </summary>
    Task<ProviderPayment> PayCardAsync(string reference, CardRequest request, CancellationToken ct);

    Task<ProviderPayment> GetAsync(string reference, CancellationToken ct);

    /// <summary>Cancela a cobrança que ainda não pagou (o PIX, o cartão em análise).</summary>
    /// <exception cref="PaymentProviderRejectedException">Já não se cancela (pago, aprovado ou recusado).</exception>
    Task<ProviderPayment> CancelAsync(string reference, CancellationToken ct);

    /// <summary>Devolve <paramref name="amountCents"/> (nulo: o que falta) do que foi pago (liquidado).</summary>
    Task<ProviderRefund> RefundAsync(string reference, long? amountCents, string idempotencyKey, CancellationToken ct);

    /// <summary>
    /// A notificação do cartão traz só um token: o provedor diz quais cobranças mudaram. A verdade de cada uma
    /// vem do <see cref="GetAsync"/>.
    /// </summary>
    Task<IReadOnlyList<string>> ChangedByNotificationAsync(string token, CancellationToken ct);
}
