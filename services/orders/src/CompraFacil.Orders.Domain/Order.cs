namespace CompraFacil.Orders.Domain;

public enum OrderStatus
{
    Pending,
    Confirmed,
    Cancelled,
}

/// <summary>Por que um pedido foi cancelado (contrato: CancellationReason).</summary>
public enum CancellationReason
{
    /// <summary>O inventory recusou a reserva.</summary>
    OutOfStock,

    /// <summary>O payments recusou a cobrança.</summary>
    PaymentFailed,
}

/// <summary>
/// Pedido de compra e dono da SAGA. Nasce <see cref="OrderStatus.Pending"/> e termina
/// <see cref="OrderStatus.Confirmed"/> (estoque reservado e pagamento aprovado) ou
/// <see cref="OrderStatus.Cancelled"/>. Os estados finais não mudam mais.
/// </summary>
public sealed class Order
{
    public const int MaxItems = 50;

    // não readonly: o EF Core materializa a lista (coluna jsonb) direto no campo
    private List<OrderItem> _items = [];

    private Order()
    {
        // EF Core
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string CustomerEmail { get; private set; } = "";
    public OrderStatus Status { get; private set; }
    public CancellationReason? CancellationReason { get; private set; }

    /// <summary>Motivo da recusa do pagamento (contrato FailureReason), quando cancelado por pagamento.</summary>
    public string? PaymentFailureReason { get; private set; }

    public Guid? PaymentId { get; private set; }
    public IReadOnlyList<OrderItem> Items => _items;
    public Money Total { get; private set; }

    /// <summary>Chave do Idempotency-Key e o hash do corpo que a criou.</summary>
    public string IdempotencyKey { get; private set; } = "";
    public string RequestHash { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Concorrência otimista (xmin do Postgres): duas transições simultâneas não passam juntas.</summary>
    public uint Version { get; private set; }

    /// <summary>Cria o pedido pendente com itens já validados e precificados pelo catálogo.</summary>
    public static Order Place(
        Guid id, Guid customerId, string customerEmail, IReadOnlyList<OrderItem> items,
        string idempotencyKey, string requestHash, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count is 0 or > MaxItems)
        {
            throw new DomainException($"o pedido precisa de 1 a {MaxItems} itens");
        }
        if (items.Select(i => i.ProductId).Distinct().Count() != items.Count)
        {
            throw new DomainException("produto repetido no pedido");
        }
        if (items.Any(i => i.Quantity is < OrderItem.MinQuantity or > OrderItem.MaxQuantity))
        {
            throw new DomainException($"quantidade deve estar entre {OrderItem.MinQuantity} e {OrderItem.MaxQuantity}");
        }

        var order = new Order
        {
            Id = id,
            CustomerId = customerId,
            CustomerEmail = customerEmail,
            Status = OrderStatus.Pending,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            CreatedAt = now,
            UpdatedAt = now,
        };
        order._items.AddRange(items);
        order.Total = items.Aggregate(Money.Zero(items[0].UnitPrice.Currency), (sum, i) => sum.Plus(i.LineTotal));
        return order;
    }

    /// <summary>Pagamento aprovado: fim feliz da SAGA.</summary>
    public void Confirm(Guid paymentId, DateTimeOffset now)
    {
        EnsurePending("confirmar");
        Status = OrderStatus.Confirmed;
        PaymentId = paymentId;
        UpdatedAt = now;
    }

    /// <summary>Estoque recusado ou pagamento recusado: a SAGA termina com compensação.</summary>
    public void Cancel(CancellationReason reason, DateTimeOffset now, Guid? paymentId = null, string? paymentFailureReason = null)
    {
        EnsurePending("cancelar");
        if (reason == Domain.CancellationReason.PaymentFailed && string.IsNullOrWhiteSpace(paymentFailureReason))
        {
            throw new DomainException("cancelamento por pagamento precisa do motivo da recusa");
        }
        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        PaymentId = paymentId;
        PaymentFailureReason = reason == Domain.CancellationReason.PaymentFailed ? paymentFailureReason : null;
        UpdatedAt = now;
    }

    private void EnsurePending(string action)
    {
        if (Status != OrderStatus.Pending)
        {
            throw new DomainException($"não é possível {action} o pedido {Id}: já está {Status}");
        }
    }
}
