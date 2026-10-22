namespace CompraFacil.Orders.Domain;

/// <summary>Item do pedido com nome e preço congelados no momento da compra.</summary>
public sealed record OrderItem(
    Guid ProductId,
    string Sku,
    string Name,
    int Quantity,
    Money UnitPrice,
    Guid? CampaignId)
{
    public const int MinQuantity = 1;
    public const int MaxQuantity = 99;

    public Money LineTotal => UnitPrice.Times(Quantity);
}
