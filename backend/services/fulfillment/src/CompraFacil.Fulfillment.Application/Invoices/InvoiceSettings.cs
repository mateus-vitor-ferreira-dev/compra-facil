namespace CompraFacil.Fulfillment.Application.Invoices;

/// <summary>
/// O que a aplicação precisa saber da nota (seção Invoice): se emite e o NCM de cada produto, pela categoria
/// dele no fornecedor. Sem o mapeamento, vale o NCM padrão (o contador confirma os códigos na venda de verdade).
/// </summary>
public sealed class InvoiceSettings
{
    public bool Enabled { get; set; } = true;
    public string DefaultNcm { get; set; } = "85176299";
    public Dictionary<string, string> NcmByCategory { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string NcmFor(string? category) =>
        category is not null && NcmByCategory.TryGetValue(category, out var ncm) ? ncm : DefaultNcm;
}
