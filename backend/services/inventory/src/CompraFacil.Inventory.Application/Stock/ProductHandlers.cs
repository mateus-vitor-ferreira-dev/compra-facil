using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Inventory.Domain;
using Microsoft.Extensions.Options;

namespace CompraFacil.Inventory.Application.Stock;

public sealed class InventoryOptions
{
    /// <summary>
    /// Estoque inicial de produto novo do catálogo. Em produção é 0 (entra por ajuste); no ambiente
    /// local o compose usa um valor positivo para os 10 mil produtos do seed poderem ser vendidos.
    /// </summary>
    public int InitialOnHandForNewProducts { get; set; }
}

/// <summary>
/// ProductCreated → cria o saldo do produto: o inicial configurado, ou, no produto de fornecedor, o espelho
/// da última leitura dele (zero até a primeira chegar).
/// </summary>
public sealed class ProductCreated(IStockRepository stock, IOptions<InventoryOptions> options, TimeProvider clock)
    : IHandleEvent<ProductCreatedData>
{
    public async Task HandleAsync(ProductCreatedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (await stock.FindAsync(data.ProductId, ct) is not null)
        {
            return;
        }
        var now = clock.GetUtcNow();
        if (data.Supplier is not { } supplier)
        {
            stock.Add(StockItem.Create(data.ProductId, options.Value.InitialOnHandForNewProducts, now));
            await stock.SaveChangesAsync(ct);
            return;
        }
        var item = StockItem.Create(data.ProductId, 0, now);
        item.LinkToSupplier(supplier.Name, supplier.Vid, now);
        if ((await stock.GetSupplierLevelsAsync(supplier.Name, [supplier.Vid], ct)).SingleOrDefault() is { } level)
        {
            item.Mirror(level.Available, now); // a leitura do fornecedor chegou antes do produto
        }
        stock.Add(item);
        await stock.SaveChangesAsync(ct);
    }
}

/// <summary>
/// ProductUpdated → liga ao fornecedor o saldo de um produto que ainda não estava ligado: produto criado
/// antes de o snapshot levar o fornecedor (#218) passa a espelhar na primeira alteração dele.
/// </summary>
public sealed class ProductUpdated(IStockRepository stock, TimeProvider clock) : IHandleEvent<ProductCreatedData>
{
    public Task HandleAsync(ProductCreatedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Supplier is not { } supplier)
        {
            return Task.CompletedTask;
        }
        return OptimisticConcurrency.RetryAsync(stock.Reset, async () =>
        {
            var item = (await stock.GetForUpdateAsync([data.ProductId], ct)).SingleOrDefault();
            if (item is null || item.IsMirrored)
            {
                return false; // o ProductCreated cria (e liga); ligado, nada a fazer
            }
            var now = clock.GetUtcNow();
            item.LinkToSupplier(supplier.Name, supplier.Vid, now);
            var level = (await stock.GetSupplierLevelsAsync(supplier.Name, [supplier.Vid], ct)).SingleOrDefault();
            item.Mirror(level?.Available ?? 0, now);
            await stock.SaveChangesAsync(ct);
            return true;
        });
    }
}

/// <summary>ProductDeleted → o produto não aceita mais reservas (as feitas seguem o fluxo).</summary>
public sealed class ProductDeleted(IStockRepository stock, TimeProvider clock) : IHandleEvent<ProductDeletedData>
{
    public Task HandleAsync(ProductDeletedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return OptimisticConcurrency.RetryAsync(stock.Reset, async () =>
        {
            var now = clock.GetUtcNow();
            var item = (await stock.GetForUpdateAsync([data.ProductId], ct)).SingleOrDefault();
            if (item is null)
            {
                item = StockItem.Create(data.ProductId, 0, now);
                stock.Add(item);
            }
            item.Discontinue(now);
            await stock.SaveChangesAsync(ct);
            return true;
        });
    }
}
