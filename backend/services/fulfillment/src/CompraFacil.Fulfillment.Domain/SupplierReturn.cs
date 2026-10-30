namespace CompraFacil.Fulfillment.Domain;

public enum SupplierReturnStatus
{
    /// <summary>Na fila para abrir no fornecedor.</summary>
    Pending,

    /// <summary>Aberta no fornecedor, esperando a resposta dele.</summary>
    Open,

    /// <summary>O fornecedor aceitou a devolução.</summary>
    Approved,

    /// <summary>O fornecedor recusou: a loja já estornou o cliente, e a equipe resolve o prejuízo com ele.</summary>
    Rejected,
}

/// <summary>O que mudou numa devolução, para o serviço publicar o evento certo.</summary>
public enum SupplierReturnChange
{
    None,
    Opened,
    Resolved,
}

/// <summary>
/// A devolução ao fornecedor de um pedido que o cliente desistiu (#223). Id = order_id. O estorno ao cliente já
/// saiu, sem esperar por ela: isto é a pendência da loja com o fornecedor. A resposta dele pode chegar antes de
/// a abertura ser gravada (webhook), então abrir não desfaz uma resolução.
/// </summary>
public sealed class SupplierReturn
{
    private List<SupplierOrderItem> _items = [];

    private SupplierReturn()
    {
        // EF Core
    }

    public Guid Id { get; private set; }
    public string Supplier { get; private set; } = "";
    public string? SupplierOrderId { get; private set; }
    public string CustomerEmail { get; private set; } = "";
    public IReadOnlyList<SupplierOrderItem> Items => _items;
    public SupplierReturnStatus Status { get; private set; }
    public string? DisputeId { get; private set; }

    /// <summary>O código que o cliente escreve no pacote e o endereço para onde o manda.</summary>
    public string? ReturnCode { get; private set; }
    public string? ReturnAddress { get; private set; }

    /// <summary>Por que o fornecedor recusou.</summary>
    public string? ResolutionReason { get; private set; }

    /// <summary>Tentativas de abrir, com o último erro passageiro.</summary>
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? OpenedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public uint Version { get; private set; }

    public static SupplierReturn Create(SupplierOrder order, DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(order);
        var result = new SupplierReturn
        {
            Id = order.Id,
            Supplier = order.Supplier,
            SupplierOrderId = order.SupplierOrderId,
            CustomerEmail = order.CustomerEmail,
            Status = SupplierReturnStatus.Pending,
            RequestedAt = requestedAt,
        };
        result._items.AddRange(order.Items);
        return result;
    }

    public SupplierReturnChange MarkOpened(string disputeId, string returnCode, string returnAddress, DateTimeOffset now)
    {
        if (OpenedAt is not null)
        {
            return SupplierReturnChange.None;
        }
        (DisputeId, ReturnCode, ReturnAddress, OpenedAt) = (disputeId, returnCode, returnAddress, now);
        LastError = null;
        if (Status == SupplierReturnStatus.Pending)
        {
            Status = SupplierReturnStatus.Open;
        }
        return SupplierReturnChange.Opened;
    }

    public void RecordAttempt(string error)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
    }

    /// <summary>A resposta do fornecedor; a primeira vale. Recusa na abertura (sem disputa) também termina aqui.</summary>
    public SupplierReturnChange Resolve(bool approved, string? reason, DateTimeOffset now)
    {
        if (Status is SupplierReturnStatus.Approved or SupplierReturnStatus.Rejected)
        {
            return SupplierReturnChange.None;
        }
        Status = approved ? SupplierReturnStatus.Approved : SupplierReturnStatus.Rejected;
        ResolutionReason = reason is { Length: > 500 } ? reason[..500] : reason;
        ResolvedAt = now;
        return SupplierReturnChange.Resolved;
    }
}
