using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Inventory.Domain;
using Microsoft.Extensions.Logging;

namespace CompraFacil.Inventory.Application.Stock;

/// <summary>Uma variante do pedido como a conferência ao vivo a devolve.</summary>
public sealed record LiveSupplierStock(string Vid, int Available, DateTimeOffset CheckedAt);

/// <summary>
/// A conferência ao vivo no fornecedor (#218), antes de reservar e cobrar: o fulfillment pergunta a ele
/// agora. Nulo quando ela não respondeu a tempo; quem chama decide pelo espelho.
/// </summary>
public interface ISupplierStockCheck
{
    Task<IReadOnlyList<LiveSupplierStock>?> CheckAsync(IReadOnlyList<(string Vid, int Quantity)> items, CancellationToken ct);
}

/// <summary>Espelha uma leitura do fornecedor: guarda a leitura e acerta os saldos ligados à variante.</summary>
public sealed class SupplierMirror(IStockRepository stock, TimeProvider clock)
{
    /// <summary>Quem chama grava; true se a leitura era nova (não atrasada).</summary>
    public async Task<bool> ApplyAsync(string supplier, string vid, int available, DateTimeOffset checkedAt, CancellationToken ct)
    {
        var level = (await stock.GetSupplierLevelsAsync(supplier, [vid], ct)).SingleOrDefault();
        if (level is null)
        {
            stock.Add(SupplierStockLevel.Create(supplier, vid, available, checkedAt));
        }
        else if (!level.Apply(available, checkedAt))
        {
            return false;
        }
        var now = clock.GetUtcNow();
        foreach (var item in await stock.GetLinkedForUpdateAsync(supplier, vid, ct))
        {
            item.Mirror(available, now);
        }
        return true;
    }
}

/// <summary>SupplierStockChanged → o saldo do produto de dropshipping acompanha o do fornecedor.</summary>
public sealed class OnSupplierStockChanged(IStockRepository stock, SupplierMirror mirror) : IHandleEvent<SupplierStockChangedData>
{
    public Task HandleAsync(SupplierStockChangedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return OptimisticConcurrency.RetryAsync(stock.Reset, async () =>
        {
            if (await mirror.ApplyAsync(data.Supplier, data.Vid, data.Available, data.CheckedAt, ct))
            {
                await stock.SaveChangesAsync(ct);
                InventoryMetrics.SupplierStock.Add(1, new KeyValuePair<string, object?>("source", "event"));
            }
            return true;
        });
    }
}

/// <summary>
/// Antes de reservar, confere ao vivo as variantes de fornecedor de um pedido e espelha o que voltou. Se a
/// conferência não responde, o pedido segue pelo espelho (que o feed e o webhook mantêm em dia): o
/// fornecedor fora não para as vendas, e um pedido que ele não puder atender cai na SAGA (SupplierOrderFailed,
/// com estorno).
/// </summary>
public sealed partial class LiveSupplierCheck(IStockRepository stock, ISupplierStockCheck check, SupplierMirror mirror, ILogger<LiveSupplierCheck> logger)
{
    public async Task RefreshAsync(IReadOnlyList<ReservedItem> lines, CancellationToken ct)
    {
        var items = await stock.ListAsync([.. lines.Select(l => l.ProductId)], ct);
        var mirrored = items.Where(s => s.IsMirrored).ToDictionary(s => s.ProductId);
        if (mirrored.Count == 0)
        {
            return;
        }
        var wanted = lines.Where(l => mirrored.ContainsKey(l.ProductId))
            .Select(l => (mirrored[l.ProductId].SupplierVid!, l.Quantity)).ToList();
        var live = await check.CheckAsync(wanted, ct);
        if (live is null)
        {
            LogFallback(logger, wanted.Count);
            InventoryMetrics.SupplierStock.Add(1, new KeyValuePair<string, object?>("source", "fallback"));
            return;
        }
        var supplierOf = mirrored.Values.ToDictionary(s => s.SupplierVid!, s => s.Supplier!);
        // grava antes da reserva (na transação do evento): o retry da reserva limpa o que não foi gravado
        await OptimisticConcurrency.RetryAsync(stock.Reset, async () =>
        {
            foreach (var l in live.Where(l => supplierOf.ContainsKey(l.Vid)))
            {
                await mirror.ApplyAsync(supplierOf[l.Vid], l.Vid, l.Available, l.CheckedAt, ct);
            }
            await stock.SaveChangesAsync(ct);
            return true;
        });
        InventoryMetrics.SupplierStock.Add(1, new KeyValuePair<string, object?>("source", "live"));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "a conferência no fornecedor não respondeu; {Count} itens seguem pelo espelho")]
    private static partial void LogFallback(ILogger logger, int count);
}
