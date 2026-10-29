using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Inventory.Domain;

namespace CompraFacil.Inventory.Application.Stock;

/// <summary>
/// OrderPlaced → reserva todos os itens ou nenhum → StockReserved ou StockReservationFailed.
/// Roda na transação do consumer (inbox): reserva, evento e marca de processado entram juntos. Os itens
/// de fornecedor são conferidos nele, ao vivo, antes (#218): o que acabou lá recusa o pedido antes de cobrar.
/// </summary>
public sealed class ReserveStock(IStockRepository stock, IOutbox outbox, LiveSupplierCheck live, TimeProvider clock) : IHandleEvent<OrderPlacedData>
{
    public async Task HandleAsync(OrderPlacedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (await stock.GetReservationAsync(data.OrderId, forUpdate: false, ct) is not null)
        {
            return; // já reservado (evento repetido com outro event_id): nem confere de novo
        }
        await live.RefreshAsync(Lines(data), ct);
        await OptimisticConcurrency.RetryAsync(stock.Reset, () => AttemptAsync(data, ct));
    }

    private static List<ReservedItem> Lines(OrderPlacedData data) =>
        [.. data.Items.GroupBy(i => i.ProductId)
            .Select(g => new ReservedItem(g.Key, g.Sum(i => i.Quantity)))
            .OrderBy(i => i.ProductId)]; // ordem fixa: duas reservas não travam uma à outra

    private async Task<bool> AttemptAsync(OrderPlacedData data, CancellationToken ct)
    {
        if (await stock.GetReservationAsync(data.OrderId, forUpdate: false, ct) is not null)
        {
            return true; // já reservado (evento repetido com outro event_id): nada a fazer
        }
        var lines = Lines(data);
        var items = (await stock.GetForUpdateAsync([.. lines.Select(l => l.ProductId)], ct)).ToDictionary(s => s.ProductId);

        var unavailable = lines
            .Select(l => items.TryGetValue(l.ProductId, out var s)
                ? s.CanReserve(l.Quantity) ? null
                    : new UnavailableItem(l.ProductId, l.Quantity, s.Discontinued ? 0 : Math.Max(0, s.Available),
                        s.Discontinued ? UnavailableReason.UnknownProduct : UnavailableReason.InsufficientStock)
                : new UnavailableItem(l.ProductId, l.Quantity, 0, UnavailableReason.UnknownProduct))
            .OfType<UnavailableItem>()
            .ToList();

        var key = data.OrderId.ToString();
        if (unavailable.Count > 0)
        {
            outbox.Add(InventoryEvents.Topic, key, InventoryEvents.StockReservationFailed,
                new StockReservationFailedData(data.OrderId, unavailable));
        }
        else
        {
            var now = clock.GetUtcNow();
            foreach (var line in lines)
            {
                items[line.ProductId].Reserve(line.Quantity, now);
            }
            stock.Add(Reservation.Create(data.OrderId, lines, now));
            outbox.Add(InventoryEvents.Topic, key, InventoryEvents.StockReserved, new StockReservedData(data.OrderId, lines));
        }
        await stock.SaveChangesAsync(ct);
        InventoryMetrics.Reservations.Add(1, new KeyValuePair<string, object?>("result", unavailable.Count > 0 ? "failed" : "reserved"));
        return true;
    }
}

/// <summary>OrderConfirmed → a reserva vira baixa definitiva (sem evento).</summary>
public sealed class CommitReservation(IStockRepository stock, TimeProvider clock) : IHandleEvent<OrderConfirmedData>
{
    public Task HandleAsync(OrderConfirmedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return OptimisticConcurrency.RetryAsync(stock.Reset, async () =>
        {
            var reservation = await stock.GetReservationAsync(data.OrderId, forUpdate: true, ct);
            if (reservation is not { Status: ReservationStatus.Reserved })
            {
                return false; // sem reserva ou já tratada
            }
            var now = clock.GetUtcNow();
            var items = (await stock.GetForUpdateAsync([.. reservation.Items.Select(i => i.ProductId)], ct)).ToDictionary(s => s.ProductId);
            foreach (var item in reservation.Items)
            {
                items[item.ProductId].Commit(item.Quantity, now);
            }
            reservation.Commit(now);
            await stock.SaveChangesAsync(ct);
            return true;
        });
    }
}

/// <summary>
/// OrderCancelled → devolve a reserva ao estoque e publica StockReleased (compensação). Sem reserva
/// (o pedido foi recusado por falta de estoque), não há o que devolver e nada é publicado.
/// </summary>
public sealed class ReleaseReservation(IStockRepository stock, IOutbox outbox, TimeProvider clock) : IHandleEvent<OrderCancelledData>
{
    public Task HandleAsync(OrderCancelledData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return OptimisticConcurrency.RetryAsync(stock.Reset, async () =>
        {
            var reservation = await stock.GetReservationAsync(data.OrderId, forUpdate: true, ct);
            if (reservation is not { Status: ReservationStatus.Reserved })
            {
                return false;
            }
            var now = clock.GetUtcNow();
            var items = (await stock.GetForUpdateAsync([.. reservation.Items.Select(i => i.ProductId)], ct)).ToDictionary(s => s.ProductId);
            foreach (var item in reservation.Items)
            {
                items[item.ProductId].Release(item.Quantity, now);
            }
            reservation.Release(now);
            outbox.Add(InventoryEvents.Topic, data.OrderId.ToString(), InventoryEvents.StockReleased,
                new StockReleasedData(data.OrderId, reservation.Items));
            await stock.SaveChangesAsync(ct);
            InventoryMetrics.Reservations.Add(1, new KeyValuePair<string, object?>("result", "released"));
            return true;
        });
    }
}
