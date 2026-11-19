namespace CompraFacil.Orders.Domain;

/// <summary>
/// Item do pedido com nome e preço congelados no momento da compra. Size é o tamanho escolhido, como está no
/// catálogo (nulo no produto sem tamanho e nos pedidos de antes do campo): o mesmo produto pode vir em mais de
/// um item, um por tamanho. Personalization é o texto do cliente para a arte (ex.: o nome na estampa), normalizado;
/// nulo no produto sem personalização: o mesmo produto e tamanho pode vir em mais de um item, um por texto.
/// </summary>
public sealed record OrderItem(
    Guid ProductId,
    string Sku,
    string Name,
    int Quantity,
    Money UnitPrice,
    Guid? CampaignId,
    string? Size = null,
    string? Personalization = null)
{
    public const int MinQuantity = 1;
    public const int MaxQuantity = 99;
    public const int MaxSizeLength = 10;

    /// <summary>A linha do pedido: o produto, o tamanho (sem diferenciar maiúsculas) e o texto da personalização.</summary>
    public static (Guid ProductId, string Size, string Personalization) LineKey(Guid productId, string? size, string? personalization = null) =>
        (productId, (size ?? "").ToUpperInvariant(), Domain.Personalization.Normalize(personalization) ?? "");

    public Money LineTotal => UnitPrice.Times(Quantity);
}
