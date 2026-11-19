using CompraFacil.Orders.Domain;

namespace CompraFacil.Orders.Application.Catalog;

/// <summary>
/// Item a precificar: o que o cliente pediu (Size nulo no produto sem tamanho; Personalization, o texto normalizado,
/// nulo no produto sem personalização).
/// </summary>
public sealed record ItemToPrice(
    Guid ProductId, int Quantity, long? ExpectedUnitPriceCents, string? Size = null, string? Personalization = null);

public enum PricedItemStatus
{
    Ok,
    NotFound,
    NotActive,
    PriceChanged,
    InvalidQuantity,
    /// <summary>Tamanho que o produto não tem, ou faltando no produto que tem tamanho.</summary>
    InvalidSize,
    /// <summary>Texto que o produto não aceita: faltando, longo, com caractere que a arte não imprime, ou sobrando.</summary>
    InvalidPersonalization,
}

/// <summary>
/// Resposta do catálogo para um item (sku, nome e preço vêm mesmo quando há problema). Size é o tamanho como
/// está no catálogo (nulo no produto sem tamanho); Personalization, o texto normalizado (nulo sem personalização).
/// </summary>
public sealed record PricedItem(
    Guid ProductId,
    PricedItemStatus Status,
    int Quantity,
    string Sku,
    string Name,
    Money? UnitPrice,
    Guid? CampaignId,
    string? Size = null,
    string? Personalization = null);

/// <summary>
/// Validação e preço efetivo (com campanha) dos itens no catálogo: gRPC ValidateItems.
/// Lança <c>DependencyUnavailableException</c> quando o catálogo não responde.
/// </summary>
public interface ICatalogPricing
{
    Task<IReadOnlyList<PricedItem>> PriceAsync(IReadOnlyList<ItemToPrice> items, CancellationToken ct);
}
