using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application.Invoices;

/// <summary>Resposta da emissão: autorizada pela SEFAZ (com a chave) ou rejeitada (com o cStat e o motivo).</summary>
public abstract record InvoiceIssue
{
    public sealed record Authorized(string ProviderId, string AccessKey, int Number, int Series, DateTimeOffset IssuedAt) : InvoiceIssue;

    public sealed record Rejected(string? ProviderId, int Code, string Reason) : InvoiceIssue;
}

/// <summary>
/// O emissor de NF-e (ADR 0007): a Nuvem Fiscal (em dev, o invoice-sim no contrato dela). Erro passageiro
/// (fora do ar, timeout, 5xx) sai como <see cref="InvoiceIssuerUnavailableException"/> e é tentado de novo;
/// a emissão repetida do mesmo pedido devolve a nota já emitida (a referência é o order_id).
/// </summary>
public interface IInvoiceIssuer
{
    Task<InvoiceIssue> IssueAsync(Invoice invoice, CancellationToken ct);

    /// <summary>
    /// A NF-e de devolução da nota autorizada (#223): entrada, referenciando a chave da nota de venda. Como a
    /// emissão, repetir devolve a mesma nota.
    /// </summary>
    Task<InvoiceIssue> IssueReturnAsync(Invoice invoice, CancellationToken ct);

    /// <summary>O XML autorizado (nfeProc), para o fornecedor.</summary>
    Task<string> GetXmlAsync(string providerId, CancellationToken ct);

    /// <summary>O DANFE em PDF, para o cliente.</summary>
    Task<byte[]> GetPdfAsync(string providerId, CancellationToken ct);

    /// <summary>Cancela a nota autorizada. Recusa do emissor (fora do prazo) sai como <see cref="InvoiceCancelRefusedException"/>.</summary>
    Task CancelAsync(string providerId, string justification, CancellationToken ct);
}

public sealed class InvoiceIssuerUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>O emissor não cancela (passou o prazo de 24 h, por exemplo): a equipe resolve com uma nota de entrada.</summary>
public sealed class InvoiceCancelRefusedException(string message) : Exception(message);

public interface IInvoiceRepository
{
    void Add(Invoice invoice);
    Task<Invoice?> GetAsync(Guid orderId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
