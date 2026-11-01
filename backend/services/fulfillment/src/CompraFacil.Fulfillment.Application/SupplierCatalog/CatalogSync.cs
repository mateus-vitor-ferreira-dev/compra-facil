using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;
using Microsoft.Extensions.Logging;

namespace CompraFacil.Fulfillment.Application.SupplierCatalog;

public interface ISupplierProductRepository
{
    /// <summary>
    /// Roda <paramref name="work"/> numa transação com o lock da sincronização (o job e o botão do painel,
    /// em qualquer réplica, não correm juntos) e grava no fim. False se outra sincronização está rodando.
    /// </summary>
    Task<bool> RunExclusiveAsync(Func<Task> work, CancellationToken ct);

    Task<List<SupplierProduct>> ListAsync(string supplier, CancellationToken ct);

    /// <summary>Os produtos destes vids (rastreados, para alterar); vid fora do retrato não volta.</summary>
    Task<List<SupplierProduct>> GetManyAsync(string supplier, IReadOnlyCollection<string> vids, CancellationToken ct);

    /// <summary>Quantos produtos estão no feed agora e quando o estoque foi lido por último.</summary>
    Task<(int InFeed, DateTimeOffset? LastStockReadAt)> FeedSummaryAsync(string supplier, CancellationToken ct);

    void Add(SupplierProduct product);

    Task SaveChangesAsync(CancellationToken ct);
}

public sealed record CatalogSyncResult(int Created, int Updated, int Removed, int Unchanged);

/// <summary>Outra sincronização do catálogo já está rodando.</summary>
public sealed class CatalogSyncBusyException() : Exception("a sincronização do catálogo do fornecedor já está rodando");

/// <summary>
/// Lê o feed inteiro do fornecedor e publica o que mudou desde a última leitura: SupplierProductUpdated
/// (novo, de volta ou alterado) e SupplierProductRemoved (saiu do feed). O feed é lido antes de tudo:
/// uma falha no meio não tira nada de venda. Feed vazio com produtos no retrato é tratado como erro do
/// fornecedor, não como "parou de vender tudo".
/// </summary>
public sealed partial class CatalogSync(
    ISupplierGateway supplier, ISupplierProductRepository products, IOutbox outbox, TimeProvider clock, ILogger<CatalogSync> logger)
{
    /// <exception cref="SupplierUnavailableException">O feed não pôde ser lido; nada mudou.</exception>
    /// <exception cref="CatalogSyncBusyException">Outra sincronização está rodando.</exception>
    public async Task<CatalogSyncResult> RunAsync(CancellationToken ct)
    {
        var feed = (await supplier.ListProductsAsync(ct))
            .GroupBy(i => i.Vid).Select(g => g.Last()) // vid repetido no feed: vale o último
            .ToDictionary(i => i.Vid);
        // a disponibilidade de cada variante; sem ela (fornecedor lento), o catálogo segue e o estoque espera
        IReadOnlyDictionary<string, int>? stock = null;
        var stockAt = clock.GetUtcNow();
        try
        {
            stock = feed.Count == 0 ? null : await supplier.GetStockAsync([.. feed.Keys], ct);
        }
        catch (SupplierUnavailableException e)
        {
            LogStockUnavailable(logger, e, supplier.Name);
        }
        CatalogSyncResult? result = null;
        var ran = await products.RunExclusiveAsync(async () =>
        {
            var now = clock.GetUtcNow();
            var known = (await products.ListAsync(supplier.Name, ct)).ToDictionary(p => p.Vid);
            if (feed.Count == 0 && known.Values.Any(p => p.InFeed))
            {
                throw new SupplierUnavailableException("o fornecedor devolveu o feed vazio; nada foi tirado de venda");
            }
            int created = 0, updated = 0, removed = 0, unchanged = 0;
            foreach (var item in feed.Values)
            {
                if (!known.TryGetValue(item.Vid, out var product))
                {
                    product = SupplierProduct.Create(supplier.Name, item, now);
                    products.Add(product);
                    known[item.Vid] = product;
                    Publish(product, SupplierProductChange.Updated);
                    created++;
                    continue;
                }
                var change = product.Apply(item, now);
                Publish(product, change);
                _ = change == SupplierProductChange.None ? unchanged++ : updated++;
            }
            foreach (var product in known.Values.Where(p => !feed.ContainsKey(p.Vid)))
            {
                var change = product.Remove(now);
                Publish(product, change);
                if (change == SupplierProductChange.Removed)
                {
                    removed++;
                }
            }
            foreach (var (vid, available) in stock ?? new Dictionary<string, int>())
            {
                if (known.TryGetValue(vid, out var product) && product.UpdateStock(available, stockAt))
                {
                    SupplierStock.Publish(outbox, product);
                }
            }
            result = new CatalogSyncResult(created, updated, removed, unchanged);
        }, ct);
        if (!ran)
        {
            throw new CatalogSyncBusyException();
        }
        LogSynced(logger, supplier.Name, result!.Created, result.Updated, result.Removed, result.Unchanged);
        FulfillmentMetrics.CatalogSynced(supplier.Name, result);
        return result;
    }

    private void Publish(SupplierProduct p, SupplierProductChange change)
    {
        switch (change)
        {
            case SupplierProductChange.Updated:
                outbox.Add(SupplierCatalogEvents.Topic, p.Vid, SupplierCatalogEvents.SupplierProductUpdated,
                    new SupplierProductUpdatedData(p.Supplier, p.Vid, p.Sku, p.Name, p.Category, new Money(p.CostCents, p.Currency),
                        p.ImageUrl, p.WeightGrams, p.UpdatedAt));
                break;
            case SupplierProductChange.Removed:
                outbox.Add(SupplierCatalogEvents.Topic, p.Vid, SupplierCatalogEvents.SupplierProductRemoved,
                    new SupplierProductRemovedData(p.Supplier, p.Vid, p.RemovedAt!.Value));
                break;
            case SupplierProductChange.None:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, null);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "estoque do fornecedor {Supplier} não respondeu; o catálogo segue, a disponibilidade fica para a próxima leitura")]
    private static partial void LogStockUnavailable(ILogger logger, Exception e, string supplier);

    [LoggerMessage(Level = LogLevel.Information, Message = "catálogo do fornecedor {Supplier}: {Created} novos, {Updated} alterados, {Removed} fora do feed, {Unchanged} iguais")]
    private static partial void LogSynced(ILogger logger, string supplier, int created, int updated, int removed, int unchanged);
}

/// <summary>Tópico e tipos de evento do catálogo do fornecedor (contracts/asyncapi/fulfillment.v1.yaml).</summary>
public static class SupplierCatalogEvents
{
    public const string Topic = "fulfillment.supplier-product.v1";
    public const string SupplierProductUpdated = nameof(SupplierProductUpdated);
    public const string SupplierProductRemoved = nameof(SupplierProductRemoved);
    public const string SupplierStockChanged = nameof(SupplierStockChanged);
}

/// <summary>O produto como está no feed agora (novo, de volta ou alterado). Chave = vid.</summary>
public sealed record SupplierProductUpdatedData(
    string Supplier, string Vid, string Sku, string Name, string Category, Money Cost, string? ImageUrl, int? WeightGrams, DateTimeOffset UpdatedAt);

/// <summary>O produto saiu do feed: o fornecedor deixou de vender. Chave = vid.</summary>
public sealed record SupplierProductRemovedData(string Supplier, string Vid, DateTimeOffset RemovedAt);
