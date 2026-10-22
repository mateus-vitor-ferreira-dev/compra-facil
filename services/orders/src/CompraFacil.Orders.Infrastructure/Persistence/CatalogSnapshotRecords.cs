namespace CompraFacil.Orders.Infrastructure.Persistence;

/// <summary>Cópia local de um produto do catálogo (catalog.product.v1).</summary>
public sealed class ProductSnapshotRecord
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid CategoryId { get; set; }
    public long PriceCents { get; set; }
    public string Currency { get; set; } = "";
    public string Status { get; set; } = "";
    public int Version { get; set; }
    /// <summary>Removido no catálogo; a linha fica para barrar eventos mais velhos.</summary>
    public bool Deleted { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Cópia local de uma campanha (catalog.campaign.v1, ADR 0002).</summary>
public sealed class CampaignSnapshotRecord
{
    public Guid CampaignId { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public int DiscountPercent { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public List<Guid> ProductIds { get; set; } = [];
    public int Version { get; set; }
    public bool Deleted { get; set; }
}
