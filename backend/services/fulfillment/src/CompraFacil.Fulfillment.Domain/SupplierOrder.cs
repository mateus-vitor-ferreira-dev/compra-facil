namespace CompraFacil.Fulfillment.Domain;

public enum SupplierOrderStatus
{
    /// <summary>Na fila para ir ao fornecedor.</summary>
    Pending,

    /// <summary>Reservado por um despachante, que está chamando o fornecedor (ou caiu no meio).</summary>
    Placing,

    /// <summary>O fornecedor aceitou o pedido.</summary>
    Placed,

    Shipped,
    Delivered,

    /// <summary>O fornecedor recusou, cancelou ou não respondeu depois de todas as tentativas.</summary>
    Failed,
}

/// <summary>Por que o pedido não chegou ao fornecedor (contrato: SupplierOrderFailed.reason).</summary>
public static class SupplierFailureReasons
{
    public const string OutOfStock = "out_of_stock";
    public const string Rejected = "rejected";
    public const string SupplierCancelled = "supplier_cancelled";
    public const string SupplierUnavailable = "supplier_unavailable";

    /// <summary>A SEFAZ rejeitou a nota da loja: o pedido não vai ao fornecedor (#221).</summary>
    public const string InvoiceRejected = "invoice_rejected";
}

/// <summary>Um item como vai ao fornecedor: o SKU dele (por ora, o da loja), o nome e a quantidade.</summary>
public sealed record SupplierOrderItem(string Sku, string Name, int Quantity);

/// <summary>Para quem e para onde o fornecedor manda (#221): o nome, o CPF (o consigneeID do CJ), o telefone e o endereço.</summary>
public sealed record ShipTo(
    string Name, string Cpf, string Phone, string ZipCode, string Street, string Number, string? Complement,
    string District, string City, string State);

/// <summary>Rastreio do envio, como o fornecedor manda.</summary>
public sealed record Tracking(string Number, string? Provider, string? Url);

/// <summary>O que mudou num pedido, para o serviço publicar o evento certo (nenhum = nada mudou).</summary>
public enum SupplierOrderChange
{
    None,
    Placed,
    Shipped,
    Delivered,
    Failed,
}

/// <summary>
/// O pedido da loja repassado ao fornecedor (Id = order_id). Só anda para a frente: um webhook atrasado
/// ou repetido não desfaz o que já aconteceu (entregue não volta a enviado; enviado não vira recusado).
/// O fornecedor deduplica pelo número do pedido, então reenviar um pedido que ficou em
/// <see cref="SupplierOrderStatus.Placing"/> é seguro.
/// </summary>
public sealed class SupplierOrder
{
    public const int MaxAttempts = 8;

    private SupplierOrder()
    {
        // EF Core
    }

    public Guid Id { get; private set; }
    public string CustomerEmail { get; private set; } = "";
    private readonly List<SupplierOrderItem> _items = [];
    public IReadOnlyList<SupplierOrderItem> Items => _items;

    /// <summary>Nulo só nos pedidos confirmados antes de o checkout pedir o endereço.</summary>
    public ShipTo? ShipTo { get; private set; }
    public SupplierOrderStatus Status { get; private set; }
    public string Supplier { get; private set; } = "";
    public string? SupplierOrderId { get; private set; }
    public Tracking? Tracking { get; private set; }
    public string? FailureReason { get; private set; }
    public string? FailureDetail { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    /// <summary>Depois de um erro passageiro, quando tentar de novo (espera crescente, até 5 minutos).</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? PlacedAt { get; private set; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }
    public uint Version { get; private set; }

    public static SupplierOrder Create(
        Guid orderId, string customerEmail, IReadOnlyList<SupplierOrderItem> items, string supplier, DateTimeOffset now, ShipTo? shipTo = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("o pedido precisa de pelo menos um item", nameof(items));
        }
        var order = new SupplierOrder
        {
            Id = orderId,
            CustomerEmail = customerEmail,
            ShipTo = shipTo,
            Supplier = supplier,
            Status = SupplierOrderStatus.Pending,
            CreatedAt = now,
        };
        order._items.AddRange(items);
        return order;
    }

    private bool Finished => Status is SupplierOrderStatus.Delivered or SupplierOrderStatus.Failed;

    /// <summary>O fornecedor aceitou (resposta da criação ou o primeiro webhook).</summary>
    public SupplierOrderChange MarkPlaced(string supplierOrderId, DateTimeOffset now)
    {
        SupplierOrderId ??= supplierOrderId;
        if (Status is not (SupplierOrderStatus.Pending or SupplierOrderStatus.Placing))
        {
            return SupplierOrderChange.None;
        }
        Status = SupplierOrderStatus.Placed;
        PlacedAt = now;
        LastError = null;
        return SupplierOrderChange.Placed;
    }

    /// <summary>Recusa de negócio (sem estoque, pedido recusado, cancelado pelo fornecedor): não tenta de novo.</summary>
    public SupplierOrderChange Fail(string reason, string? detail, DateTimeOffset now)
    {
        // depois de enviado, o problema é de entrega (devolução), não de pedido recusado
        if (Finished || Status is SupplierOrderStatus.Shipped)
        {
            return SupplierOrderChange.None;
        }
        Status = SupplierOrderStatus.Failed;
        FailureReason = reason;
        FailureDetail = detail is { Length: > 500 } ? detail[..500] : detail;
        FailedAt = now;
        return SupplierOrderChange.Failed;
    }

    /// <summary>Erro passageiro (fornecedor fora, timeout): volta para a fila, ou desiste depois do limite.</summary>
    public SupplierOrderChange MarkAttemptFailed(string error, DateTimeOffset now)
    {
        if (Status is not (SupplierOrderStatus.Pending or SupplierOrderStatus.Placing))
        {
            return SupplierOrderChange.None;
        }
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        if (Attempts >= MaxAttempts)
        {
            return Fail(SupplierFailureReasons.SupplierUnavailable, LastError, now);
        }
        Status = SupplierOrderStatus.Pending;
        NextAttemptAt = now + TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Attempts) * 5));
        return SupplierOrderChange.None;
    }

    public SupplierOrderChange MarkShipped(Tracking tracking, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tracking);
        if (Finished || Status is SupplierOrderStatus.Shipped)
        {
            return SupplierOrderChange.None;
        }
        Status = SupplierOrderStatus.Shipped;
        Tracking = tracking;
        PlacedAt ??= now;
        ShippedAt = now;
        return SupplierOrderChange.Shipped;
    }

    public SupplierOrderChange MarkDelivered(DateTimeOffset now)
    {
        if (Finished)
        {
            return SupplierOrderChange.None;
        }
        Status = SupplierOrderStatus.Delivered;
        PlacedAt ??= now;
        ShippedAt ??= now;
        DeliveredAt = now;
        return SupplierOrderChange.Delivered;
    }
}
