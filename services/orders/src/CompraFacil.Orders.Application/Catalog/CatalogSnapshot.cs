using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Orders.Domain;

namespace CompraFacil.Orders.Application.Catalog;

/// <summary>Eventos do catalog que o orders consome (contracts/asyncapi/catalog.v1.yaml).</summary>
public static class CatalogEvents
{
    public const string ProductsTopic = "catalog.product.v1";
    public const string CampaignsTopic = "catalog.campaign.v1";
}

/// <summary>data de ProductCreated/ProductUpdated (ProductSnapshot do contrato).</summary>
public sealed record ProductSnapshotData(
    Guid ProductId, string Sku, string Name, Guid CategoryId, Money Price, string Status, int Version, DateTimeOffset UpdatedAt);

public sealed record ProductDeletedData(Guid ProductId, string Sku, int Version);

/// <summary>data de CampaignCreated/CampaignUpdated (CampaignSnapshot do contrato, ADR 0002).</summary>
public sealed record CampaignSnapshotData(
    Guid CampaignId, string Slug, string Name, int DiscountPercent,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, IReadOnlyList<Guid> ProductIds, int Version);

public sealed record CampaignDeletedData(Guid CampaignId, int Version);

/// <summary>
/// Cópia local do catálogo, para o orders validar pedidos quando o gRPC do catálogo está fora
/// (fallback na #81). Cada escrita só vale se a versão for maior que a guardada: evento atrasado
/// ou repetido não desfaz um mais novo. Remoção vira marca (tombstone), para o mesmo motivo.
/// </summary>
public interface ICatalogSnapshotStore
{
    Task UpsertProductAsync(ProductSnapshotData product, CancellationToken ct);

    Task DeleteProductAsync(ProductDeletedData product, CancellationToken ct);

    Task UpsertCampaignAsync(CampaignSnapshotData campaign, CancellationToken ct);

    Task DeleteCampaignAsync(CampaignDeletedData campaign, CancellationToken ct);
}

public sealed class ProductChanged(ICatalogSnapshotStore store) : IHandleEvent<ProductSnapshotData>
{
    public Task HandleAsync(ProductSnapshotData data, EventContext context, CancellationToken ct) =>
        store.UpsertProductAsync(data, ct);
}

public sealed class ProductDeleted(ICatalogSnapshotStore store) : IHandleEvent<ProductDeletedData>
{
    public Task HandleAsync(ProductDeletedData data, EventContext context, CancellationToken ct) =>
        store.DeleteProductAsync(data, ct);
}

public sealed class CampaignChanged(ICatalogSnapshotStore store) : IHandleEvent<CampaignSnapshotData>
{
    public Task HandleAsync(CampaignSnapshotData data, EventContext context, CancellationToken ct) =>
        store.UpsertCampaignAsync(data, ct);
}

public sealed class CampaignDeleted(ICatalogSnapshotStore store) : IHandleEvent<CampaignDeletedData>
{
    public Task HandleAsync(CampaignDeletedData data, EventContext context, CancellationToken ct) =>
        store.DeleteCampaignAsync(data, ct);
}
