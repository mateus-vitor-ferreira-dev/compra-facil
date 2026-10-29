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
            var reason = order.CancellationReason?.ToString() ?? "-";
            OrdersMetrics.Finished.Add(1, new KeyValuePair<string, object?>("status", order.Status.ToString()), new KeyValuePair<string, object?>("reason", reason));
            if (order.Status == OrderStatus.Confirmed)
            {
                OrdersMetrics.ConfirmedAmount.Add(order.Total.AmountCents);
            }
            return true;
        });

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Because} para pedido desconhecido {OrderId}; ignorado")]
    private static partial void LogUnknownOrder(ILogger logger, Guid orderId, string because);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Because} para o pedido {OrderId}, que já está {Status}; ignorado")]
    private static partial void LogAlreadyFinished(ILogger logger, Guid orderId, OrderStatus status, string because);
}

/// <summary>
/// StockReserved → o pedido passa para a etapa da cobrança. Não publica nada (quem segue a SAGA é o
/// payments); só registra a etapa para o cliente acompanhar o pedido ao vivo.
/// </summary>
public sealed partial class OnStockReserved(IOrderRepository orders, TimeProvider clock, ILogger<OnStockReserved> logger)
    : IHandleEvent<StockReservedData>
{
    public Task HandleAsync(StockReservedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return OptimisticConcurrency.RetryAsync(orders.Reset, async () =>
        {
            var order = await orders.GetForUpdateAsync(data.OrderId, ct);
            if (order is null)
            {
                LogUnknownOrder(logger, data.OrderId);
                return false;
            }
            // pedido que já terminou (o pagamento chegou antes, por outro tópico) ou reserva repetida
            if (!order.MarkStockReserved(clock.GetUtcNow()))
            {
                return false;
            }
            await orders.SaveChangesAsync(ct);
            return true;
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "StockReserved para pedido desconhecido {OrderId}; ignorado")]
    private static partial void LogUnknownOrder(ILogger logger, Guid orderId);
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

/// <summary>
/// Depois de pago, o pedido segue o fornecedor (eventos do fulfillment) e o estorno (do payments). Cada
/// transição grava o pedido e o evento juntos; a que não muda nada (repetida, atrasada, de pedido que já
/// terminou) não publica.
/// </summary>
public sealed partial class OrderProgress(IOrderRepository orders, IOutbox outbox, TimeProvider clock, ILogger<OrderProgress> logger)
{
    /// <summary>Aplica a transição; <paramref name="publish"/> dá o evento dela (tipo e data), ou nulo se não publica.</summary>
    public Task ApplyAsync(Guid orderId, string because, Func<Order, DateTimeOffset, bool> transition,
        Func<Order, (string Type, object Data)?> publish, CancellationToken ct) =>
        OptimisticConcurrency.RetryAsync(orders.Reset, async () =>
        {
            var order = await orders.GetForUpdateAsync(orderId, ct);
            if (order is null)
            {
                LogUnknownOrder(logger, orderId, because);
                return false;
            }
            if (!transition(order, clock.GetUtcNow()))
            {
                LogIgnored(logger, orderId, order.Status, because);
                return false;
            }
            if (publish(order) is { } e)
            {
                outbox.Add(OrderEvents.Topic, order.Id.ToString(), e.Type, e.Data);
            }
            await orders.SaveChangesAsync(ct);
            OrdersMetrics.Progressed.Add(1, new KeyValuePair<string, object?>("step", because));
            return true;
        });

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Because} para pedido desconhecido {OrderId}; ignorado")]
    private static partial void LogUnknownOrder(ILogger logger, Guid orderId, string because);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Because} para o pedido {OrderId}, que está {Status}; nada muda")]
    private static partial void LogIgnored(ILogger logger, Guid orderId, OrderStatus status, string because);
}

/// <summary>SupplierOrderPlaced → o fornecedor aceitou. Não publica (o pedido ao vivo relê o banco).</summary>
public sealed class OnSupplierOrderPlaced(OrderProgress progress) : IHandleEvent<SupplierOrderPlacedData>
{
    public Task HandleAsync(SupplierOrderPlacedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return progress.ApplyAsync(data.OrderId, "SupplierOrderPlaced", (order, now) => order.MarkSentToSupplier(now), _ => null, ct);
    }
}

/// <summary>SupplierOrderShipped → enviado, com o rastreio → OrderShipped.</summary>
public sealed class OnSupplierOrderShipped(OrderProgress progress) : IHandleEvent<SupplierOrderShippedData>
{
    public Task HandleAsync(SupplierOrderShippedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        var tracking = new Tracking(data.TrackingNumber, data.TrackingProvider, data.TrackingUrl);
        return progress.ApplyAsync(data.OrderId, "SupplierOrderShipped", (order, now) => order.Ship(tracking, now),
            order => (OrderEvents.OrderShipped, OrderShippedData.From(order)), ct);
    }
}

/// <summary>SupplierOrderDelivered → entregue → OrderDelivered: fim feliz.</summary>
public sealed class OnSupplierOrderDelivered(OrderProgress progress) : IHandleEvent<SupplierOrderDeliveredData>
{
    public Task HandleAsync(SupplierOrderDeliveredData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return progress.ApplyAsync(data.OrderId, "SupplierOrderDelivered", (order, now) => order.Deliver(now),
            order => (OrderEvents.OrderDelivered, OrderDeliveredData.From(order)), ct);
    }
}

/// <summary>
/// SupplierOrderFailed → cancelado já pago → OrderCancelled (supplier_failed), que faz o payments
/// estornar e o notifications avisar. A reserva já virou baixa: o inventory não tem o que liberar.
/// </summary>
public sealed class OnSupplierOrderFailed(OrderProgress progress) : IHandleEvent<SupplierOrderFailedData>
{
    public Task HandleAsync(SupplierOrderFailedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return progress.ApplyAsync(data.OrderId, "SupplierOrderFailed", (order, now) => order.FailAtSupplier(now, data.InvoiceRejected),
            order => (OrderEvents.OrderCancelled, OrderCancelledData.From(order)), ct);
    }
}

/// <summary>InvoiceIssued → a nota fiscal do pedido foi autorizada: o cliente a vê no pedido. Não publica.</summary>
public sealed class OnInvoiceIssued(OrderProgress progress) : IHandleEvent<InvoiceIssuedData>
{
    public Task HandleAsync(InvoiceIssuedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        var invoice = new OrderInvoice(data.AccessKey, data.Number, data.Series, data.IssuedAt);
        return progress.ApplyAsync(data.OrderId, "InvoiceIssued", (order, now) => order.RecordInvoice(invoice, now), _ => null, ct);
    }
}

/// <summary>InvoiceCancelled → a nota foi cancelada depois do estorno. Não publica.</summary>
public sealed class OnInvoiceCancelled(OrderProgress progress) : IHandleEvent<InvoiceCancelledData>
{
    public Task HandleAsync(InvoiceCancelledData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return progress.ApplyAsync(data.OrderId, "InvoiceCancelled", (order, now) => order.CancelInvoice(data.CancelledAt, now), _ => null, ct);
    }
}

/// <summary>PaymentPending → o PIX foi gerado: o pedido mostra o QR ao cliente (ao vivo). Não publica.</summary>
public sealed class OnPaymentPending(OrderProgress progress) : IHandleEvent<PaymentPendingData>
{
    public Task HandleAsync(PaymentPendingData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        var pix = new OrderPix(data.Pix.QrCode, data.Pix.QrCodeBase64, data.Pix.TicketUrl, data.Pix.ExpiresAt);
        return progress.ApplyAsync(data.OrderId, "PaymentPending", (order, now) => order.AwaitPix(pix, now), _ => null, ct);
    }
}

/// <summary>PaymentRefunded → o estorno do pedido cancelado depois de pago voltou ao cliente.</summary>
public sealed class OnPaymentRefunded(OrderProgress progress) : IHandleEvent<PaymentRefundedData>
{
    public Task HandleAsync(PaymentRefundedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        // estorno parcial (devolução de um item, pelo painel) não é o estorno do pedido cancelado
        return data.Full == false
            ? Task.CompletedTask
            : progress.ApplyAsync(data.OrderId, "PaymentRefunded", (order, now) => order.MarkRefunded(now),
                order => (OrderEvents.OrderRefunded, OrderRefundedData.From(order)), ct);
    }
}
