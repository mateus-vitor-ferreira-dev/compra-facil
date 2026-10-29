namespace CompraFacil.Fulfillment.Domain;

public enum InvoiceStatus
{
    /// <summary>Esperando a emissão: o despachante emite antes de mandar o pedido ao fornecedor.</summary>
    Pending,

    /// <summary>A SEFAZ autorizou: a nota tem chave de acesso e vai ao fornecedor com o pedido.</summary>
    Authorized,

    /// <summary>A SEFAZ rejeitou: o pedido não vai ao fornecedor, e o cliente é estornado.</summary>
    Rejected,

    /// <summary>Cancelada depois do estorno ao cliente.</summary>
    Cancelled,
}

/// <summary>Um item da nota: o produto, o NCM dele, a quantidade e os valores em centavos.</summary>
public sealed record InvoiceLine(string Sku, string Name, string Ncm, int Quantity, long UnitPriceCents, long TotalCents);

/// <summary>O que mudou na nota, para o serviço publicar o evento certo.</summary>
public enum InvoiceChange
{
    None,
    Issued,
    Rejected,
    Cancelled,
}

/// <summary>
/// A NF-e de venda do pedido (#221, ADR 0007): a loja é a vendedora e emite antes do despacho. Id = order_id.
/// Nasce pendente com o pedido pago; o despachante a emite e só então manda o pedido ao fornecedor. O estorno
/// total ao cliente pede o cancelamento, que outro laço faz (o emissor é um serviço externo). Só anda para a
/// frente: autorizada não volta a pendente, cancelada não muda mais.
/// </summary>
public sealed class Invoice
{
    // não readonly: o EF Core materializa a lista (coluna jsonb) direto no campo
    private List<InvoiceLine> _lines = [];

    private Invoice()
    {
        // EF Core
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string CustomerEmail { get; private set; } = "";
    public ShipTo ShipTo { get; private set; } = null!;
    public IReadOnlyList<InvoiceLine> Lines => _lines;
    public long TotalCents { get; private set; }

    /// <summary>pix ou card: a forma de pagamento da nota (tPag).</summary>
    public string PaymentMethod { get; private set; } = "";

    public InvoiceStatus Status { get; private set; }

    /// <summary>O id da nota no emissor.</summary>
    public string? ProviderId { get; private set; }

    /// <summary>A chave de acesso de 44 dígitos.</summary>
    public string? AccessKey { get; private set; }
    public int? Number { get; private set; }
    public int? Series { get; private set; }
    public DateTimeOffset? IssuedAt { get; private set; }

    /// <summary>O cStat e o motivo da rejeição da SEFAZ.</summary>
    public int? RejectionCode { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }

    /// <summary>O estorno total pediu o cancelamento; o laço do cancelamento a pega.</summary>
    public DateTimeOffset? CancelRequestedAt { get; private set; }
    public DateTimeOffset? CancelClaimedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>O emissor recusou o cancelamento (ex.: fora do prazo): fica para a equipe resolver.</summary>
    public string? CancelError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public uint Version { get; private set; }

    public static Invoice Create(
        Guid orderId, Guid customerId, string customerEmail, ShipTo shipTo, IReadOnlyList<InvoiceLine> lines,
        string paymentMethod, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(shipTo);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new ArgumentException("a nota precisa de pelo menos um item", nameof(lines));
        }
        var invoice = new Invoice
        {
            Id = orderId,
            CustomerId = customerId,
            CustomerEmail = customerEmail,
            ShipTo = shipTo,
            TotalCents = lines.Sum(l => l.TotalCents),
            PaymentMethod = paymentMethod,
            Status = InvoiceStatus.Pending,
            CreatedAt = now,
        };
        invoice._lines.AddRange(lines);
        return invoice;
    }

    public InvoiceChange MarkAuthorized(string providerId, string accessKey, int number, int series, DateTimeOffset issuedAt)
    {
        if (Status != InvoiceStatus.Pending)
        {
            return InvoiceChange.None;
        }
        Status = InvoiceStatus.Authorized;
        (ProviderId, AccessKey, Number, Series, IssuedAt) = (providerId, accessKey, number, series, issuedAt);
        return InvoiceChange.Issued;
    }

    public InvoiceChange MarkRejected(string? providerId, int code, string reason, DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Pending)
        {
            return InvoiceChange.None;
        }
        Status = InvoiceStatus.Rejected;
        ProviderId = providerId;
        RejectionCode = code;
        RejectionReason = reason.Length > 500 ? reason[..500] : reason;
        RejectedAt = now;
        return InvoiceChange.Rejected;
    }

    /// <summary>O cliente foi estornado: a nota autorizada precisa ser cancelada. False se não há o que cancelar.</summary>
    public bool RequestCancel(DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Authorized || CancelRequestedAt is not null)
        {
            return false;
        }
        CancelRequestedAt = now;
        return true;
    }

    public InvoiceChange MarkCancelled(DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Authorized)
        {
            return InvoiceChange.None;
        }
        Status = InvoiceStatus.Cancelled;
        CancelledAt = now;
        CancelError = null;
        return InvoiceChange.Cancelled;
    }

    public void MarkCancelFailed(string error) => CancelError = error.Length > 500 ? error[..500] : error;
}
