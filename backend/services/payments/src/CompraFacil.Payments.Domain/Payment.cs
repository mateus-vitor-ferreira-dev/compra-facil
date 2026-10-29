namespace CompraFacil.Payments.Domain;

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

    /// <summary>Cartão autorizado: o valor está garantido, a captura vem quando o fornecedor aceita.</summary>
    Authorized,

    /// <summary>Pago (PIX) ou capturado (cartão); pode ter estorno parcial (<see cref="Payment.RefundedCents"/>).</summary>
    Approved,

    Failed,

    /// <summary>Autorização (ou PIX não pago) cancelada: nada foi cobrado.</summary>
    Voided,

    /// <summary>Todo o valor voltou ao cliente.</summary>
    Refunded,
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

/// <summary>Um estorno aplicado: a chave da requisição e a referência no provedor, para o mesmo não contar duas vezes.</summary>
public sealed record RefundEntry(string Key, string Reference, long AmountCents, DateTimeOffset At);

/// <summary>O PIX: o copia-e-cola, o QR em PNG (base64), a página de pagamento e a validade.</summary>
public sealed record PixCharge(string QrCode, string QrCodeBase64, string? TicketUrl, DateTimeOffset ExpiresAt);

/// <summary>
/// Uma cobrança no provedor (#220). PIX: nasce processing, fica pending com o QR e termina approved (pago)
/// ou failed (expirou). Cartão: authorized (o valor garantido) e approved na captura, ou voided se o pedido
/// cair antes; recusado é failed. O aprovado pode ser estornado em partes até o total.
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

    /// <summary>Provedor usado (mercadopago).</summary>
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

    public void Fail(FailureReason reason, string? providerReference, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            throw new DomainException($"pagamento {Id} já está {Status}");
        }
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        ProviderReference ??= providerReference;
        UpdatedAt = now;
    }

    /// <summary>Cancela a autorização (ou o PIX não pago): o cliente não é cobrado.</summary>
    public void Void(DateTimeOffset now)
    {
        if (Status is not (PaymentStatus.Authorized or PaymentStatus.Pending))
        {
            throw new DomainException($"pagamento {Id} está {Status}: só a autorização ou o PIX pendente se cancelam");
        }
        Status = PaymentStatus.Voided;
        RefundedAt = now;
        UpdatedAt = now;
    }

    /// <summary>O estorno desta chave, se já foi aplicado (a requisição repetida).</summary>
    public RefundEntry? RefundOf(string key) => _refunds.FirstOrDefault(r => r.Key == key);

    /// <summary>
    /// Devolve <paramref name="amountCents"/> do que foi pago; o total vira Refunded. False se o estorno desta
    /// chave (ou desta referência no provedor) já foi aplicado.
    /// </summary>
    public bool Refund(long amountCents, string key, string refundReference, DateTimeOffset now)
    {
        if (_refunds.Any(r => r.Key == key || r.Reference == refundReference))
        {
            return false;
        }
        Ensure(PaymentStatus.Approved);
        if (amountCents <= 0 || amountCents > Amount.AmountCents - RefundedCents)
        {
            throw new DomainException($"pagamento {Id}: estorno de {amountCents} com {Amount.AmountCents - RefundedCents} a devolver");
        }
        RefundedCents += amountCents;
        _refunds.Add(new RefundEntry(key, refundReference, amountCents, now));
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
