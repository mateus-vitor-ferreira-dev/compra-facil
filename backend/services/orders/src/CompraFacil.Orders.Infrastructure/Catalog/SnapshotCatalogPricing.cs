using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Orders.Application.Catalog;
using CompraFacil.Orders.Domain;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Orders.Infrastructure.Catalog;

/// <summary>
/// Preço pela cópia local do catálogo (catalog.product.v1 e catalog.campaign.v1), com as mesmas
/// regras do ValidateItems do catalog (services/catalog/internal/service/validate_items.go): vale a
/// campanha vigente de maior desconto (empate: a que termina antes), arredondando a favor do cliente.
/// Produto que a cópia ainda não conhece não é recusado como inexistente: pode ser só atraso dos
/// eventos, então o pedido segue indisponível (503), como antes do fallback.
/// </summary>
internal sealed class SnapshotCatalogPricing(OrdersDbContext db, TimeProvider clock) : ICatalogPricing
{
    private const string ActiveStatus = "active";

    public async Task<IReadOnlyList<PricedItem>> PriceAsync(IReadOnlyList<ItemToPrice> items, CancellationToken ct)
    {
        var ids = items.Select(i => i.ProductId).ToList();
        var products = await db.ProductSnapshots.AsNoTracking()
            .Where(p => ids.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, ct);
        var unknown = ids.Where(id => !products.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new DependencyUnavailableException(
                $"catálogo indisponível e sem cópia local de {string.Join(", ", unknown)}", ResilientCatalogPricing.RetryAfter);
        }

        var now = clock.GetUtcNow();
        var campaigns = await db.CampaignSnapshots.AsNoTracking()
            .Where(c => !c.Deleted && c.StartsAt <= now && c.EndsAt > now && c.ProductIds.Any(id => ids.Contains(id)))
            .ToListAsync(ct);

        return [.. items.Select(item =>
        {
            var product = products[item.ProductId];
            if (product.Deleted)
            {
                return new PricedItem(item.ProductId, PricedItemStatus.NotFound, item.Quantity, product.Sku, product.Name, null, null);
            }
            var campaign = campaigns
                .Where(c => c.ProductIds.Contains(item.ProductId))
                .OrderByDescending(c => c.DiscountPercent).ThenBy(c => c.EndsAt)
                .FirstOrDefault();
            var cents = campaign is null ? product.PriceCents : product.PriceCents * (100 - campaign.DiscountPercent) / 100;
            var status = product.Status != ActiveStatus ? PricedItemStatus.NotActive
                : item.Quantity is < OrderItem.MinQuantity or > OrderItem.MaxQuantity ? PricedItemStatus.InvalidQuantity
                : item.ExpectedUnitPriceCents is { } expected && expected != cents ? PricedItemStatus.PriceChanged
                : PricedItemStatus.Ok;
            return new PricedItem(item.ProductId, status, item.Quantity, product.Sku, product.Name,
                new Money(cents, product.Currency), campaign?.CampaignId);
        })];
    }
}
