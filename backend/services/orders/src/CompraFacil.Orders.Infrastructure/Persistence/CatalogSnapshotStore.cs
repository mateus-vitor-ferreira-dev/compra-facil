using CompraFacil.Orders.Application.Catalog;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Orders.Infrastructure.Persistence;

/// <summary>
/// Grava o snapshot no DbContext do escopo do consumer: o SaveChanges (e o registro no inbox) é
/// feito pelo EventConsumer, na mesma transação.
/// </summary>
internal sealed class CatalogSnapshotStore(OrdersDbContext db) : ICatalogSnapshotStore
{
    public async Task UpsertProductAsync(ProductSnapshotData product, CancellationToken ct)
    {
        var row = await db.ProductSnapshots.FindAsync([product.ProductId], ct);
        if (row is not null && row.Version >= product.Version)
        {
            return; // evento atrasado ou repetido
        }
        if (row is null)
        {
            row = new ProductSnapshotRecord { ProductId = product.ProductId };
            db.ProductSnapshots.Add(row);
        }
        row.Sku = product.Sku;
        row.Name = product.Name;
        row.CategoryId = product.CategoryId;
        row.PriceCents = product.Price.AmountCents;
        row.Currency = product.Price.Currency;
        row.Status = product.Status;
        row.Version = product.Version;
        row.Deleted = false;
        row.UpdatedAt = product.UpdatedAt;
    }

    public async Task DeleteProductAsync(ProductDeletedData product, CancellationToken ct)
    {
        var row = await db.ProductSnapshots.FindAsync([product.ProductId], ct);
        if (row is not null && row.Version >= product.Version)
        {
            return;
        }
        if (row is null)
        {
            // remoção chegou antes da criação (ou a criação nunca virá): a marca barra a criação velha
            row = new ProductSnapshotRecord { ProductId = product.ProductId, Sku = product.Sku };
            db.ProductSnapshots.Add(row);
        }
        row.Version = product.Version;
        row.Deleted = true;
    }

    public async Task UpsertCampaignAsync(CampaignSnapshotData campaign, CancellationToken ct)
    {
        var row = await db.CampaignSnapshots.FindAsync([campaign.CampaignId], ct);
        if (row is not null && row.Version >= campaign.Version)
        {
            return;
        }
        if (row is null)
        {
            row = new CampaignSnapshotRecord { CampaignId = campaign.CampaignId };
            db.CampaignSnapshots.Add(row);
        }
        row.Slug = campaign.Slug;
        row.Name = campaign.Name;
        row.DiscountPercent = campaign.DiscountPercent;
        row.StartsAt = campaign.StartsAt;
        row.EndsAt = campaign.EndsAt;
        row.ProductIds = [.. campaign.ProductIds];
        row.Version = campaign.Version;
        row.Deleted = false;
    }

    public async Task DeleteCampaignAsync(CampaignDeletedData campaign, CancellationToken ct)
    {
        var row = await db.CampaignSnapshots.FindAsync([campaign.CampaignId], ct);
        if (row is not null && row.Version >= campaign.Version)
        {
            return;
        }
        if (row is null)
        {
            row = new CampaignSnapshotRecord { CampaignId = campaign.CampaignId };
            db.CampaignSnapshots.Add(row);
        }
        row.Version = campaign.Version;
        row.Deleted = true;
        row.ProductIds = [];
    }
}
