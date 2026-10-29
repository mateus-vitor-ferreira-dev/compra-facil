namespace CompraFacil.Orders.Domain;

public enum OrderStatus
{
    Pending,

    /// <summary>Pago: com o fornecedor (<see cref="Order.SentToSupplierAt"/> marca quando ele aceitou).</summary>
    Confirmed,

    /// <summary>O fornecedor despachou, com o rastreio.</summary>
    Shipped,

    Delivered,
    Cancelled,
}

/// <summary>Por que um pedido foi cancelado (contrato: CancellationReason).</summary>
public enum CancellationReason
{
    /// <summary>O inventory recusou a reserva.</summary>
    OutOfStock,

    /// <summary>O payments recusou a cobrança.</summary>
    PaymentFailed,

    /// <summary>Depois de pago, o fornecedor não atendeu: o cliente é estornado.</summary>
    SupplierFailed,
}

/// <summary>Rastreio do envio, como o fornecedor mandou.</summary>
public sealed record Tracking(string Number, string? Provider, string? Url);

/// <summary>
/// Pedido de compra e dono da SAGA. Nasce <see cref="OrderStatus.Pending"/>; com o estoque reservado e o
/// pagamento aprovado fica <see cref="OrderStatus.Confirmed"/> e vai ao fornecedor, que o leva a
/// <see cref="OrderStatus.Shipped"/> e <see cref="OrderStatus.Delivered"/>. Termina
/// <see cref="OrderStatus.Cancelled"/> se o estoque ou o pagamento forem recusados, ou se o fornecedor
/// não atender (aí já pago, e o cliente é estornado). Só anda para a frente: entregue e cancelado não
/// mudam mais. No pendente, o <see cref="StockReservedAt"/> marca a etapa: sem ele, o estoque está sendo
/// reservado; com ele, o pagamento está sendo cobrado.
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

    /// <summary>Quando o inventory reservou o estoque (nulo: ainda não reservou, ou recusou).</summary>
    public DateTimeOffset? StockReservedAt { get; private set; }

    /// <summary>Quando o fornecedor aceitou o pedido pago.</summary>
    public DateTimeOffset? SentToSupplierAt { get; private set; }

    public Tracking? Tracking { get; private set; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>Quando o payments devolveu o valor ao cliente (pedido cancelado depois de pago).</summary>
    public DateTimeOffset? RefundedAt { get; private set; }

    /// <summary>Cancelado depois de pago e o estorno ainda não voltou.</summary>
    public bool AwaitingRefund => Status == OrderStatus.Cancelled && CancellationReason == Domain.CancellationReason.SupplierFailed && RefundedAt is null;
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

    /// <summary>
    /// Estoque reservado: a SAGA passa para a cobrança. Devolve false se não mudou nada (o pedido já
    /// terminou ou a reserva já estava marcada), para o evento repetido ou atrasado não virar transição.
    /// </summary>
    public bool MarkStockReserved(DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending || StockReservedAt is not null)
        {
            return false;
        }
        StockReservedAt = now;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Pagamento aprovado: fim feliz da SAGA.</summary>
    public void Confirm(Guid paymentId, DateTimeOffset now)
    {
        EnsurePending("confirmar");
        Status = OrderStatus.Confirmed;
        PaymentId = paymentId;
        // o payments só cobra com o estoque reservado; o StockReserved pode chegar depois (outro tópico)
        StockReservedAt ??= now;
        UpdatedAt = now;
    }

    /// <summary>
    /// O fornecedor aceitou o pedido pago. As transições do fornecedor devolvem false se não mudou nada
    /// (evento repetido, atrasado ou de um pedido que já terminou), para não virarem evento de novo.
    /// </summary>
    public bool MarkSentToSupplier(DateTimeOffset now)
    {
        if (Status != OrderStatus.Confirmed || SentToSupplierAt is not null)
        {
            return false;
        }
        SentToSupplierAt = now;
        UpdatedAt = now;
        return true;
    }

    /// <summary>O fornecedor despachou.</summary>
    public bool Ship(Tracking tracking, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tracking);
        if (Status != OrderStatus.Confirmed)
        {
            return false;
        }
        Status = OrderStatus.Shipped;
        Tracking = tracking;
        SentToSupplierAt ??= now;
        ShippedAt = now;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Chegou ao cliente: fim feliz.</summary>
    public bool Deliver(DateTimeOffset now)
    {
        if (Status is not (OrderStatus.Confirmed or OrderStatus.Shipped))
        {
            return false;
        }
        Status = OrderStatus.Delivered;
        SentToSupplierAt ??= now;
        ShippedAt ??= now;
        DeliveredAt = now;
        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// O fornecedor recusou, cancelou ou não respondeu (o motivo fica no fulfillment): cancelado já pago,
    /// e o OrderCancelled faz o payments estornar. Depois de enviado não se aplica (vira devolução).
    /// </summary>
    public bool FailAtSupplier(DateTimeOffset now)
    {
        if (Status != OrderStatus.Confirmed)
        {
            return false;
        }
        Status = OrderStatus.Cancelled;
        CancellationReason = Domain.CancellationReason.SupplierFailed;
        UpdatedAt = now;
        return true;
    }

    /// <summary>O payments devolveu o valor ao cliente.</summary>
    public bool MarkRefunded(DateTimeOffset now)
    {
        if (!AwaitingRefund)
        {
            return false;
        }
        RefundedAt = now;
        UpdatedAt = now;
        return true;
    }

    /// <summary>Estoque recusado ou pagamento recusado: a SAGA termina com compensação.</summary>
    public void Cancel(CancellationReason reason, DateTimeOffset now, Guid? paymentId = null, string? paymentFailureReason = null)
    {
        EnsurePending("cancelar");
        if (reason == Domain.CancellationReason.SupplierFailed)
        {
            throw new DomainException("o pedido pendente ainda não foi ao fornecedor");
        }
        if (reason == Domain.CancellationReason.PaymentFailed && string.IsNullOrWhiteSpace(paymentFailureReason))
        {
            throw new DomainException("cancelamento por pagamento precisa do motivo da recusa");
        }
        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        PaymentId = paymentId;
        PaymentFailureReason = reason == Domain.CancellationReason.PaymentFailed ? paymentFailureReason : null;
        if (reason == Domain.CancellationReason.PaymentFailed)
        {
            StockReservedAt ??= now; // recusa de pagamento: houve reserva (que o OrderCancelled libera)
        }
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
