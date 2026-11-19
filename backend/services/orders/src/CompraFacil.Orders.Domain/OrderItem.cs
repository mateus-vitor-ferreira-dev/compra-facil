namespace CompraFacil.Orders.Domain;

/// <summary>
/// Item do pedido com nome e preço congelados no momento da compra. Size é o tamanho escolhido, como está no
/// catálogo (nulo no produto sem tamanho e nos pedidos de antes do campo): o mesmo produto pode vir em mais de
/// um item, um por tamanho.
/// </summary>
public sealed record OrderItem(
    Guid ProductId,
    string Sku,
    string Name,
    int Quantity,
    Money UnitPrice,
    Guid? CampaignId,
    string? Size = null)
{
    public const int MinQuantity = 1;
    public const int MaxQuantity = 99;
    public const int MaxSizeLength = 10;

    /// <summary>A linha do pedido: o produto e o tamanho (sem diferenciar maiúsculas).</summary>
    public static (Guid ProductId, string Size) LineKey(Guid productId, string? size) =>
        (productId, (size ?? "").ToUpperInvariant());

    public Money LineTotal => UnitPrice.Times(Quantity);
}
