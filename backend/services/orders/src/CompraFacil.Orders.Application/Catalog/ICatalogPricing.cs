using CompraFacil.Orders.Domain;

namespace CompraFacil.Orders.Application.Catalog;

/// <summary>Item a precificar: o que o cliente pediu.</summary>
public sealed record ItemToPrice(Guid ProductId, int Quantity, long? ExpectedUnitPriceCents);

public enum PricedItemStatus
{
    Ok,
    NotFound,
    NotActive,
    PriceChanged,
    InvalidQuantity,
}

/// <summary>Resposta do catálogo para um item (sku, nome e preço vêm mesmo quando há problema).</summary>
public sealed record PricedItem(
    Guid ProductId,
    PricedItemStatus Status,
    int Quantity,
    string Sku,
    string Name,
    Money? UnitPrice,
    Guid? CampaignId);

/// <summary>
/// Validação e preço efetivo (com campanha) dos itens no catálogo: gRPC ValidateItems.
/// Lança <c>DependencyUnavailableException</c> quando o catálogo não responde.
/// </summary>
public interface ICatalogPricing
{
    Task<IReadOnlyList<PricedItem>> PriceAsync(IReadOnlyList<ItemToPrice> items, CancellationToken ct);
}
