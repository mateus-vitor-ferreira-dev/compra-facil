namespace CompraFacil.Fulfillment.Domain;

/// <summary>Um item do feed do fornecedor, como ele manda (custo na moeda dele, em centavos).</summary>
public sealed record SupplierFeedItem(
    string Vid, string Sku, string Name, string Category, long CostCents, string Currency, string? ImageUrl, int? WeightGrams);

public enum SupplierProductChange
{
    None,

    /// <summary>Novo no feed, voltou ao feed ou mudou (custo, nome, categoria, foto, peso).</summary>
    Updated,

    /// <summary>Saiu do feed: o fornecedor deixou de vender.</summary>
    Removed,
}

/// <summary>
/// O último retrato de um produto do fornecedor (chave: fornecedor + vid). Serve para publicar só o que
/// mudou entre uma leitura do feed e a seguinte; quem decide o preço de venda é o catalog.
/// </summary>
public sealed class SupplierProduct
{
    private SupplierProduct()
    {
        // EF Core
    }

    public string Supplier { get; private set; } = "";
    public string Vid { get; private set; } = "";
    public string Sku { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Category { get; private set; } = "";
    public long CostCents { get; private set; }
    public string Currency { get; private set; } = "";
    public string? ImageUrl { get; private set; }
    public int? WeightGrams { get; private set; }

    /// <summary>Está no feed agora (falso: saiu, em <see cref="RemovedAt"/>).</summary>
    public bool InFeed { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }

    public static SupplierProduct Create(string supplier, SupplierFeedItem item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);
        var product = new SupplierProduct { Supplier = supplier, Vid = item.Vid, FirstSeenAt = now };
        product.Apply(item, now);
        return product;
    }

    /// <summary>O item como está no feed agora.</summary>
    public SupplierProductChange Apply(SupplierFeedItem item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Vid != Vid)
        {
            throw new ArgumentException($"o item {item.Vid} não é o produto {Vid}", nameof(item));
        }
        if (item.CostCents <= 0)
        {
            throw new ArgumentException($"custo inválido no item {item.Vid}: {item.CostCents}", nameof(item));
        }
        var same = InFeed && Sku == item.Sku && Name == item.Name && Category == item.Category && CostCents == item.CostCents
            && Currency == item.Currency && ImageUrl == item.ImageUrl && WeightGrams == item.WeightGrams;
        if (same)
        {
            return SupplierProductChange.None;
        }
        (Sku, Name, Category, CostCents, Currency, ImageUrl, WeightGrams) =
            (item.Sku, item.Name, item.Category, item.CostCents, item.Currency, item.ImageUrl, item.WeightGrams);
        InFeed = true;
        RemovedAt = null;
        UpdatedAt = now;
        return SupplierProductChange.Updated;
    }

    /// <summary>Não veio no feed.</summary>
    public SupplierProductChange Remove(DateTimeOffset now)
    {
        if (!InFeed)
        {
            return SupplierProductChange.None;
        }
        InFeed = false;
        RemovedAt = now;
        UpdatedAt = now;
        return SupplierProductChange.Removed;
    }
}
