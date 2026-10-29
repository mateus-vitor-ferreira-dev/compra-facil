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

    public string? CustomerEmail { get; private set; }

    /// <summary>Como o cliente escolheu pagar (do OrderPlaced).</summary>
    public PaymentMethod Method { get; private set; }

    public int Installments { get; private set; } = 1;

    /// <summary>O token do cartão (de uso único), até a cobrança; depois é apagado.</summary>
    public string? CardToken { get; private set; }

    public string? CardBrand { get; private set; }
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

    /// <summary>OrderPlaced: valor, cliente e como pagar.</summary>
    public void RecordOrder(Guid customerId, string? customerEmail, Money amount, PaymentInstruction payment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payment);
        if (amount.AmountCents <= 0)
        {
            throw new DomainException($"pedido {OrderId} com valor inválido: {amount.AmountCents}");
        }
        if (payment.Method == PaymentMethod.Card && (string.IsNullOrWhiteSpace(payment.CardToken) || string.IsNullOrWhiteSpace(payment.CardBrand)))
        {
            throw new DomainException($"pedido {OrderId}: cartão sem token ou bandeira");
        }
        CustomerEmail = customerEmail;
        Method = payment.Method;
        Installments = payment.Method == PaymentMethod.Pix ? 1 : Math.Clamp(payment.Installments, 1, 12);
        CardToken = payment.CardToken;
        CardBrand = payment.CardBrand;
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
        CardToken = null; // de uso único: não fica guardado
        UpdatedAt = now;
    }
}

/// <summary>Como pagar, como o cliente escolheu no checkout (o payment do OrderPlaced).</summary>
public sealed record PaymentInstruction(PaymentMethod Method, int Installments, string? CardToken, string? CardBrand)
{
    public static readonly PaymentInstruction Pix = new(PaymentMethod.Pix, 1, null, null);
}
