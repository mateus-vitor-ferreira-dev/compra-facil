namespace CompraFacil.Payments.Domain;

/// <summary>
/// O que o payments sabe de um pedido antes de cobrar. Junta dois eventos que chegam em qualquer
/// ordem (tópicos diferentes): OrderPlaced (valor) e StockReserved (estoque garantido). Cobra quando
/// tem os dois; OrderCancelled antes disso descarta. Um pedido é cobrado no máximo uma vez.
/// </summary>
public sealed class Charge
{
    private Charge()
    {
        // EF Core
    }

    public Guid OrderId { get; private set; }
    public Guid? CustomerId { get; private set; }

    // valor guardado em duas colunas anuláveis (só existe depois do OrderPlaced)
    public long? AmountCents { get; private set; }
    public string? Currency { get; private set; }

    public Money? Amount => AmountCents is { } cents ? new Money(cents, Currency!) : null;
    public bool StockReserved { get; private set; }
    public bool Cancelled { get; private set; }

    /// <summary>Pagamento criado para este pedido; preenchido, não se cobra de novo.</summary>
    public Guid? PaymentId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public bool ReadyToCharge => Amount is not null && StockReserved && !Cancelled && PaymentId is null;

    public static Charge For(Guid orderId, DateTimeOffset now) =>
        new() { OrderId = orderId, CreatedAt = now, UpdatedAt = now };

    /// <summary>OrderPlaced: valor e cliente.</summary>
    public void RecordOrder(Guid customerId, Money amount, DateTimeOffset now)
    {
        if (amount.AmountCents <= 0)
        {
            throw new DomainException($"pedido {OrderId} com valor inválido: {amount.AmountCents}");
        }
        CustomerId = customerId;
        AmountCents = amount.AmountCents;
        Currency = amount.Currency;
        UpdatedAt = now;
    }

    /// <summary>StockReserved: o estoque está garantido.</summary>
    public void RecordStockReserved(DateTimeOffset now)
    {
        StockReserved = true;
        UpdatedAt = now;
    }

    /// <summary>OrderCancelled: se ainda não cobrou, não cobra mais.</summary>
    public void Cancel(DateTimeOffset now)
    {
        Cancelled = true;
        UpdatedAt = now;
    }

    public void AttachPayment(Guid paymentId, DateTimeOffset now)
    {
        if (!ReadyToCharge)
        {
            throw new DomainException($"pedido {OrderId} não está pronto para cobrança");
        }
        PaymentId = paymentId;
        UpdatedAt = now;
    }
}
