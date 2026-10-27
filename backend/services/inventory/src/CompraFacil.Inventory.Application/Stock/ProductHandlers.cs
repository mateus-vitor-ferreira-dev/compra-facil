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

/// <summary>ProductCreated → cria o saldo do produto (zerado, ou o inicial configurado).</summary>
public sealed class ProductCreated(IStockRepository stock, IOptions<InventoryOptions> options, TimeProvider clock)
    : IHandleEvent<ProductCreatedData>
{
    public async Task HandleAsync(ProductCreatedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (await stock.FindAsync(data.ProductId, ct) is null)
        {
            stock.Add(StockItem.Create(data.ProductId, options.Value.InitialOnHandForNewProducts, clock.GetUtcNow()));
            await stock.SaveChangesAsync(ct);
        }
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
