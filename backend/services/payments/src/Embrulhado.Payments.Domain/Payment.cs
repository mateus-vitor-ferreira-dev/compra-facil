namespace Embrulhado.Payments.Domain;

public enum PaymentMethod
{
    Pix,
    Card,
}

public enum PaymentStatus
{
    /// <summary>Enviado ao provedor, sem resposta definitiva (ou o cartão em análise).</summary>
    Processing,

    /// <summary>PIX gerado, esperando o cliente pagar até <see cref="PixCharge.ExpiresAt"/>.</summary>
    Pending,

    /// <summary>
    /// Cartão aprovado pela operadora e ainda não liquidado (na Efí, approved): o valor está garantido e o pedido
    /// segue, mas o dinheiro só se devolve depois da liquidação (paid), quando ele vira <see cref="Approved"/>.
    /// </summary>
    Authorized,

    /// <summary>Pago (PIX) ou cartão liquidado; pode ter estorno parcial (<see cref="Payment.RefundedCents"/>).</summary>
    Approved,

    Failed,

    /// <summary>Cobrança cancelada antes do dinheiro sair (o PIX não pago, o cartão ainda em análise): nada foi cobrado.</summary>
    Voided,

    /// <summary>Todo o valor voltou ao cliente.</summary>
    Refunded,

    /// <summary>
    /// O cliente contestou o cartão na operadora (chargeback; na Efí, contested): o dinheiro está em disputa, e quem
    /// responde é a equipe, no painel do provedor. Não se estorna pelo painel da loja (a contestação já devolve).
    /// </summary>
    Disputed,
}

/// <summary>Motivo da recusa (contrato FailureReason).</summary>
public enum FailureReason
{
    CardDeclined,
    InsufficientFunds,
    FraudSuspected,
    GatewayError,

    /// <summary>O PIX não foi pago até a expiração.</summary>
    PixExpired,
}

/// <summary>
/// Um estorno aplicado: a chave da requisição e a referência no provedor, para o mesmo não contar duas vezes.
/// <see cref="FailedAt"/>: o provedor aceitou e depois não o realizou (#396); não conta no estornado.
/// </summary>
public sealed record RefundEntry(string Key, string Reference, long AmountCents, DateTimeOffset At, DateTimeOffset? FailedAt = null);

/// <summary>
/// Um estorno pedido ao provedor e ainda sem resposta guardada: a chave, o valor, o motivo (o da devolução do
/// pedido; nulo, o do painel) e quando foi pedido. Gravado antes de chamar o provedor, para a nova tentativa não
/// pedir de novo o estorno do cartão, que na Efí não tem chave de idempotência.
/// </summary>
public sealed record PendingRefund(string Key, long AmountCents, string? Reason, DateTimeOffset RequestedAt);

/// <summary>O PIX: o copia-e-cola, o QR em PNG (base64), a página de pagamento e a validade.</summary>
public sealed record PixCharge(string QrCode, string QrCodeBase64, string? TicketUrl, DateTimeOffset ExpiresAt);

/// <summary>
/// Uma cobrança no provedor (#220, #379). PIX: nasce processing, fica pending com o QR e termina approved (pago)
/// ou failed (expirou). Cartão: processing até o provedor decidir, authorized (aprovado pela operadora) e approved
/// na liquidação; recusado é failed. O aprovado pela operadora ainda pode não se confirmar (failed, voided), e o
/// cartão autorizado ou liquidado pode ser contestado (disputed). O aprovado pode ser estornado em partes até o
/// total. A devolução pedida antes da liquidação fica em <see cref="PendingReversal"/> e sai quando ela chega.
/// </summary>
public sealed class Payment
{
    private readonly List<RefundEntry> _refunds = [];

    private Payment()
    {
        // EF Core
    }

    public IReadOnlyList<RefundEntry> Refunds => _refunds;

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Money Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public int Installments { get; private set; } = 1;
    public PaymentStatus Status { get; private set; }
    public FailureReason? FailureReason { get; private set; }

    /// <summary>Provedor usado (efi).</summary>
    public string Provider { get; private set; } = "";

    /// <summary>ID da cobrança no provedor.</summary>
    public string? ProviderReference { get; private set; }

    public string? CardLastFour { get; private set; }
    public PixCharge? Pix { get; private set; }

    /// <summary>A taxa do provedor (Payments:Fees), em centavos, para a margem real do pedido.</summary>
    public long FeeCents { get; private set; }

    public long RefundedCents { get; private set; }

    /// <summary>ID do último estorno no provedor.</summary>
    public string? RefundReference { get; private set; }

    public DateTimeOffset? RefundedAt { get; private set; }

    /// <summary>
    /// O motivo da devolução pedida quando o dinheiro ainda não podia voltar (o cartão aprovado e não liquidado:
    /// a Efí não cancela nem estorna nessa janela). Sai na liquidação; nulo, nada esperando.
    /// </summary>
    public string? PendingReversal { get; private set; }

    /// <summary>
    /// O estorno pedido ao provedor e ainda não aplicado (um de cada vez). Nulo, nenhum. Fica se a resposta do
    /// provedor não chegou (caiu no meio, ou ele não respondeu): a nova tentativa confere no provedor em vez de
    /// pedir de novo.
    /// </summary>
    public PendingRefund? PendingRefund { get; private set; }

    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Concorrência otimista (xmin): o webhook e o consumer no mesmo pagamento.</summary>
    public uint Version { get; private set; }

    /// <summary>Pago ou com o valor garantido: o pedido pode seguir.</summary>
    public bool IsPaid => Status is PaymentStatus.Authorized or PaymentStatus.Approved;

    /// <summary>Ainda sem desfecho: o webhook do provedor pode mudar.</summary>
    public bool IsOpen => Status is PaymentStatus.Processing or PaymentStatus.Pending;

    public static Payment Start(Guid id, Guid orderId, Money amount, PaymentMethod method, int installments, string provider, DateTimeOffset now)
    {
        if (installments is < 1 or > 12 || (method == PaymentMethod.Pix && installments != 1))
        {
            throw new DomainException($"parcelas inválidas para {method}: {installments}");
        }
        return new()
        {
            Id = id,
            OrderId = orderId,
            Amount = amount,
            Method = method,
            Installments = installments,
            Provider = provider,
            Status = PaymentStatus.Processing,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>O provedor deu a referência mas ainda não decidiu (cartão em análise): o webhook a encontra.</summary>
    public bool Track(string providerReference, DateTimeOffset now)
    {
        Ensure(PaymentStatus.Processing);
        if (ProviderReference == providerReference)
        {
            return false;
        }
        ProviderReference = providerReference;
        UpdatedAt = now;
        return true;
    }

    public void MarkPending(string providerReference, PixCharge pix, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pix);
        Ensure(PaymentStatus.Processing);
        Status = PaymentStatus.Pending;
        ProviderReference = providerReference;
        Pix = pix;
        UpdatedAt = now;
    }

    /// <summary>Cartão autorizado (o valor está garantido).</summary>
    public void Authorize(string providerReference, string? cardLastFour, long feeCents, DateTimeOffset now)
    {
        Ensure(PaymentStatus.Processing);
        Status = PaymentStatus.Authorized;
        ProviderReference = providerReference;
        CardLastFour = cardLastFour;
        FeeCents = feeCents;
        UpdatedAt = now;
    }

    /// <summary>PIX pago, ou cartão aprovado já capturado.</summary>
    public void Approve(string providerReference, long feeCents, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new DomainException($"pagamento {Id} já está {Status}");
        }
        Status = PaymentStatus.Approved;
        ProviderReference = providerReference;
        FeeCents = feeCents;
        CapturedAt = now;
        UpdatedAt = now;
    }

    public void Capture(DateTimeOffset now)
    {
        Ensure(PaymentStatus.Authorized);
        Status = PaymentStatus.Approved;
        CapturedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Recusado: a cobrança ainda aberta, ou o cartão aprovado que a operadora não confirmou antes da liquidação (na
    /// Efí, de approved para unpaid): o dinheiro não vem.
    /// </summary>
    public void Fail(FailureReason reason, string? providerReference, DateTimeOffset now)
    {
        if (!IsOpen && Status != PaymentStatus.Authorized)
        {
            throw new DomainException($"pagamento {Id} já está {Status}");
        }
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        ProviderReference ??= providerReference;
        UpdatedAt = now;
    }

    /// <summary>
    /// Cancela a cobrança antes do dinheiro sair (o PIX não pago, o cartão em análise, ou o aprovado que a operadora
    /// cancelou antes de liquidar): o cliente não é cobrado.
    /// </summary>
    public void Void(DateTimeOffset now)
    {
        if (Status is not (PaymentStatus.Processing or PaymentStatus.Authorized or PaymentStatus.Pending))
        {
            throw new DomainException($"pagamento {Id} está {Status}: só a cobrança que ainda não pagou se cancela");
        }
        PendingReversal = null;
        Status = PaymentStatus.Voided;
        RefundedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// O cliente contestou o cartão autorizado ou liquidado (chargeback). False se já estava contestado (a
    /// notificação repetida).
    /// </summary>
    public bool Dispute(DateTimeOffset now)
    {
        if (Status == PaymentStatus.Disputed)
        {
            return false;
        }
        if (Status is not (PaymentStatus.Authorized or PaymentStatus.Approved))
        {
            throw new DomainException($"pagamento {Id} está {Status}: só o cartão aprovado se contesta");
        }
        Status = PaymentStatus.Disputed;
        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// A devolução tem de esperar a liquidação: guarda o motivo (o primeiro vale; repetido não muda). False se já
    /// havia uma esperando.
    /// </summary>
    public bool DeferReversal(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status is not (PaymentStatus.Processing or PaymentStatus.Authorized))
        {
            throw new DomainException($"pagamento {Id} está {Status}: só o que ainda não liquidou espera para devolver");
        }
        if (PendingReversal is not null)
        {
            return false;
        }
        PendingReversal = reason;
        UpdatedAt = now;
        return true;
    }

    /// <summary>A devolução que esperava saiu (ou não há mais o que devolver: o cartão foi recusado).</summary>
    public void ClearPendingReversal(DateTimeOffset now)
    {
        if (PendingReversal is null)
        {
            return;
        }
        PendingReversal = null;
        UpdatedAt = now;
    }

    /// <summary>O estorno desta chave, se já foi aplicado e não falhou (a requisição repetida).</summary>
    public RefundEntry? RefundOf(string key) => _refunds.FirstOrDefault(r => r.Key == key && r.FailedAt is null);

    /// <summary>
    /// O provedor aceitou o estorno <paramref name="reference"/> e depois não o realizou (#396): ele sai do estornado,
    /// e o pagamento volta a ter o que devolver (o total estornado vira Approved de novo). Devolve o estorno, ou nulo
    /// se não é deste pagamento ou já foi marcado.
    /// </summary>
    public RefundEntry? RefundFailed(string reference, DateTimeOffset now)
    {
        var index = _refunds.FindIndex(r => r.Reference == reference && r.FailedAt is null);
        if (index < 0)
        {
            return null;
        }
        var failed = _refunds[index] with { FailedAt = now };
        _refunds[index] = failed;
        RefundedCents -= failed.AmountCents;
        if (Status == PaymentStatus.Refunded)
        {
            Status = PaymentStatus.Approved;
        }
        RefundedAt = _refunds.Where(r => r.FailedAt is null).Select(r => (DateTimeOffset?)r.At).LastOrDefault();
        UpdatedAt = now;
        return failed;
    }

    /// <summary>
    /// Grava o estorno que vai ser pedido ao provedor, antes de pedir. Só um de cada vez: com outro ainda sem
    /// resposta, recusa (a equipe confere no painel do provedor).
    /// </summary>
    public void RequestRefund(string key, long amountCents, string? reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (PendingRefund is { } pending)
        {
            throw new DomainException($"pagamento {Id}: o estorno {pending.Key} foi pedido ao provedor e ainda não se confirmou");
        }
        Ensure(PaymentStatus.Approved);
        if (amountCents <= 0 || amountCents > Amount.AmountCents - RefundedCents)
        {
            throw new DomainException($"pagamento {Id}: estorno de {amountCents} com {Amount.AmountCents - RefundedCents} a devolver");
        }
        PendingRefund = new PendingRefund(key, amountCents, reason, now);
        UpdatedAt = now;
    }

    /// <summary>O provedor recusou o estorno pedido: nada foi devolvido, e o pagamento pode ser estornado de novo.</summary>
    public void DropPendingRefund(DateTimeOffset now)
    {
        if (PendingRefund is null)
        {
            return;
        }
        PendingRefund = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Devolve <paramref name="amountCents"/> do que foi pago; o total vira Refunded. False se o estorno desta
    /// chave (ou desta referência no provedor) já foi aplicado.
    /// </summary>
    public bool Refund(long amountCents, string key, string refundReference, DateTimeOffset now)
    {
        // a chave de um estorno que falhou pode ser usada de novo; a referência no provedor, não
        if (_refunds.Any(r => (r.Key == key && r.FailedAt is null) || r.Reference == refundReference))
        {
            if (PendingRefund?.Key == key)
            {
                PendingRefund = null; // o pedido que ficou sem resposta era este, já aplicado
                UpdatedAt = now;
            }
            return false;
        }
        Ensure(PaymentStatus.Approved);
        if (amountCents <= 0 || amountCents > Amount.AmountCents - RefundedCents)
        {
            throw new DomainException($"pagamento {Id}: estorno de {amountCents} com {Amount.AmountCents - RefundedCents} a devolver");
        }
        RefundedCents += amountCents;
        _refunds.Add(new RefundEntry(key, refundReference, amountCents, now));
        if (PendingRefund?.Key == key)
        {
            PendingRefund = null;
        }
        RefundReference = refundReference;
        RefundedAt = now;
        if (RefundedCents == Amount.AmountCents)
        {
            Status = PaymentStatus.Refunded;
        }
        UpdatedAt = now;
        return true;
    }

    private void Ensure(PaymentStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainException($"pagamento {Id} está {Status}, não {expected}");
        }
    }
}
