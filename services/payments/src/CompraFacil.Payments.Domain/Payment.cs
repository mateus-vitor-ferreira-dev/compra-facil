namespace CompraFacil.Payments.Domain;

public enum PaymentStatus
{
    /// <summary>Enviado ao gateway.</summary>
    Processing,
    Approved,
    Failed,
}

/// <summary>Motivo da recusa (contrato FailureReason).</summary>
public enum FailureReason
{
    CardDeclined,
    InsufficientFunds,
    FraudSuspected,
    GatewayError,
}

/// <summary>Uma cobrança no gateway: nasce processing e termina approved ou failed.</summary>
public sealed class Payment
{
    private Payment()
    {
        // EF Core
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Money Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public FailureReason? FailureReason { get; private set; }

    /// <summary>Gateway usado (fake, stripe).</summary>
    public string Provider { get; private set; } = "";

    /// <summary>ID da cobrança no gateway.</summary>
    public string? ProviderReference { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Payment Start(Guid id, Guid orderId, Money amount, string provider, DateTimeOffset now) => new()
    {
        Id = id,
        OrderId = orderId,
        Amount = amount,
        Provider = provider,
        Status = PaymentStatus.Processing,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Approve(string providerReference, DateTimeOffset now)
    {
        EnsureProcessing();
        Status = PaymentStatus.Approved;
        ProviderReference = providerReference;
        UpdatedAt = now;
    }

    public void Fail(FailureReason reason, string? providerReference, DateTimeOffset now)
    {
        EnsureProcessing();
        Status = PaymentStatus.Failed;
        FailureReason = reason;
        ProviderReference = providerReference;
        UpdatedAt = now;
    }

    private void EnsureProcessing()
    {
        if (Status != PaymentStatus.Processing)
        {
            throw new DomainException($"pagamento {Id} já está {Status}");
        }
    }
}
