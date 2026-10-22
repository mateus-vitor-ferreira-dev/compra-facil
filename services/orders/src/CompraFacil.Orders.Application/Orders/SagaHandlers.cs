using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Orders.Domain;
using Microsoft.Extensions.Logging;

namespace CompraFacil.Orders.Application.Orders;

/// <summary>
/// Fecha a SAGA no orders: aplica o resultado do inventory ou do payments ao pedido e publica
/// OrderConfirmed ou OrderCancelled, na mesma transação (outbox + inbox do consumer).
/// Pedido que já terminou ignora evento atrasado; pedido desconhecido é registrado e pulado.
/// </summary>
public sealed partial class OrderOutcome(IOrderRepository orders, IOutbox outbox, TimeProvider clock, ILogger<OrderOutcome> logger)
{
    public Task ApplyAsync(Guid orderId, string because, Action<Order, DateTimeOffset> transition, CancellationToken ct) =>
        OptimisticConcurrency.RetryAsync(orders.Reset, async () =>
        {
            var order = await orders.GetForUpdateAsync(orderId, ct);
            if (order is null)
            {
                LogUnknownOrder(logger, orderId, because);
                return false;
            }
            if (order.Status != OrderStatus.Pending)
            {
                LogAlreadyFinished(logger, orderId, order.Status, because);
                return false;
            }

            transition(order, clock.GetUtcNow());
            var key = order.Id.ToString();
            if (order.Status == OrderStatus.Confirmed)
            {
                outbox.Add(OrderEvents.Topic, key, OrderEvents.OrderConfirmed, OrderConfirmedData.From(order));
            }
            else
            {
                outbox.Add(OrderEvents.Topic, key, OrderEvents.OrderCancelled, OrderCancelledData.From(order));
            }
            await orders.SaveChangesAsync(ct);
            return true;
        });

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Because} para pedido desconhecido {OrderId}; ignorado")]
    private static partial void LogUnknownOrder(ILogger logger, Guid orderId, string because);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Because} para o pedido {OrderId}, que já está {Status}; ignorado")]
    private static partial void LogAlreadyFinished(ILogger logger, Guid orderId, OrderStatus status, string because);
}

/// <summary>StockReservationFailed → pedido cancelado por falta de estoque (não há reserva a liberar).</summary>
public sealed class OnStockReservationFailed(OrderOutcome outcome) : IHandleEvent<StockReservationFailedData>
{
    public Task HandleAsync(StockReservationFailedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return outcome.ApplyAsync(data.OrderId, "StockReservationFailed",
            (order, now) => order.Cancel(CancellationReason.OutOfStock, now), ct);
    }
}

/// <summary>PaymentApproved → pedido confirmado: fim feliz da SAGA.</summary>
public sealed class OnPaymentApproved(OrderOutcome outcome) : IHandleEvent<PaymentApprovedData>
{
    public Task HandleAsync(PaymentApprovedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return outcome.ApplyAsync(data.OrderId, "PaymentApproved", (order, now) => order.Confirm(data.PaymentId, now), ct);
    }
}

/// <summary>PaymentFailed → pedido cancelado; o OrderCancelled faz o inventory liberar a reserva.</summary>
public sealed class OnPaymentFailed(OrderOutcome outcome) : IHandleEvent<PaymentFailedData>
{
    public Task HandleAsync(PaymentFailedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return outcome.ApplyAsync(data.OrderId, "PaymentFailed",
            (order, now) => order.Cancel(CancellationReason.PaymentFailed, now, data.PaymentId, data.FailureReason), ct);
    }
}
