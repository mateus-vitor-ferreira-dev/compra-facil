namespace Embrulhado.Fulfillment.Domain;

/// <summary>
/// Uma linha da planilha do fornecedor manual (#412), como a equipe subiu por último: o SKU do fornecedor (que vira
/// o SKU da loja), o custo em centavos de real e o estoque que ele informou. A planilha nova substitui a anterior.
/// </summary>
public sealed class ManualCatalogItem
{
    private ManualCatalogItem()
    {
        // EF Core
    }

    public string Sku { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Category { get; private set; } = "";
    public long CostCents { get; private set; }
    public int Stock { get; private set; }
    public int? WeightGrams { get; private set; }
    public string? ImageUrl { get; private set; }
    public DateTimeOffset ImportedAt { get; private set; }

    public static ManualCatalogItem Create(
        string sku, string name, string category, long costCents, int stock, int? weightGrams, string? imageUrl, DateTimeOffset importedAt) =>
        new()
        {
            Sku = sku,
            Name = name,
            Category = category,
            CostCents = costCents,
            Stock = stock,
            WeightGrams = weightGrams,
            ImageUrl = imageUrl,
            ImportedAt = importedAt,
        };

    /// <summary>O item como o feed do fornecedor: o SKU é o vid, e o custo, em reais.</summary>
    public SupplierFeedItem ToFeedItem() => new(Sku, Sku, Name, Category, CostCents, "BRL", ImageUrl, WeightGrams);
}
