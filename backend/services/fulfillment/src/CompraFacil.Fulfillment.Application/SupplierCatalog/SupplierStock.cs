using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application.SupplierCatalog;

/// <summary>Uma variante e quantas unidades o pedido quer dela.</summary>
public sealed record StockCheckItem(string Vid, int Quantity);

/// <summary>A disponibilidade de agora de uma variante; Known é falso para vid fora do feed.</summary>
public sealed record StockCheckResult(string Vid, int Available, bool Known);

/// <summary>
/// A disponibilidade do fornecedor espelhada na loja (#218): cada leitura (no ciclo do feed, pelo webhook
/// de estoque ou na conferência de um pedido) atualiza o retrato e publica SupplierStockChanged quando o
/// número muda. O inventory espelha o evento; a conferência responde na hora a quem vai reservar.
/// </summary>
public sealed class SupplierStock(
    ISupplierGateway supplier, ISupplierProductRepository products, ISupplierOrderRepository webhooks, IOutbox outbox, TimeProvider clock)
{
    /// <summary>Aplica as leituras ao retrato e agenda os eventos; quem chama grava (a mesma transação).</summary>
    public async Task<IReadOnlyList<SupplierProduct>> ApplyAsync(IReadOnlyDictionary<string, int> stock, DateTimeOffset checkedAt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stock);
        var known = await products.GetManyAsync(supplier.Name, [.. stock.Keys], ct);
        foreach (var p in known)
        {
            if (p.UpdateStock(stock[p.Vid], checkedAt))
            {
                Publish(outbox, p);
            }
        }
        return known;
    }

    /// <summary>Confere no fornecedor, agora, as variantes de um pedido (antes de reservar e cobrar).</summary>
    /// <exception cref="SupplierUnavailableException">O fornecedor não respondeu; quem chama decide o que fazer.</exception>
    public async Task<IReadOnlyList<StockCheckResult>> CheckAsync(IReadOnlyList<StockCheckItem> items, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(items);
        var vids = items.Select(i => i.Vid).Distinct().ToList();
        var live = await supplier.GetStockAsync(vids, ct);
        var known = (await ApplyAsync(live, clock.GetUtcNow(), ct)).Select(p => p.Vid).ToHashSet();
        await products.SaveChangesAsync(ct);
        return [.. vids.Select(v => new StockCheckResult(v, live.GetValueOrDefault(v), known.Contains(v)))];
    }

    /// <summary>O webhook de estoque do fornecedor: deduplicado pelo messageId, na mesma gravação.</summary>
    public async Task<bool> ApplyWebhookAsync(string messageId, string vid, int available, DateTimeOffset? at, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!await webhooks.TryRecordWebhookAsync(messageId, now, ct))
        {
            return false;
        }
        // a hora do webhook vem em segundos: sem o arredondamento para cima, ele perderia para uma leitura feita
        // antes dele, no mesmo segundo, e o estoque ficaria errado até o próximo ciclo do feed
        var readAt = at is { } sent && sent.AddSeconds(1) < now ? sent.AddSeconds(1) : now;
        await ApplyAsync(new Dictionary<string, int> { [vid] = available }, readAt, ct);
        await products.SaveChangesAsync(ct);
        return true;
    }

    internal static void Publish(IOutbox outbox, SupplierProduct p) =>
        outbox.Add(SupplierCatalogEvents.Topic, p.Vid, SupplierCatalogEvents.SupplierStockChanged,
            new SupplierStockChangedData(p.Supplier, p.Vid, p.Available ?? 0, p.StockCheckedAt!.Value));
}

/// <summary>A disponibilidade de uma variante mudou no fornecedor. Chave = vid.</summary>
public sealed record SupplierStockChangedData(string Supplier, string Vid, int Available, DateTimeOffset CheckedAt);
