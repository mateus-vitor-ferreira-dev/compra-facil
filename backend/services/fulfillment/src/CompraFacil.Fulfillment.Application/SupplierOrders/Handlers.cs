using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Domain;
using Microsoft.Extensions.Logging;

namespace CompraFacil.Fulfillment.Application.SupplierOrders;

/// <summary>
/// O pedido pago entra na fila do fornecedor, na transação do evento. Quem chama o fornecedor é o
/// despachante (<see cref="SupplierOrderPlacer"/>): chamar daqui prenderia o consumer num serviço
/// externo, e uma queda depois da chamada repetiria o evento sem saber se o pedido foi.
/// </summary>
public sealed partial class OnOrderConfirmed(
    ISupplierOrderRepository orders, ISupplierGateway supplier, OrderCutoff cutoff, TimeProvider clock, ILogger<OnOrderConfirmed> logger) : IHandleEvent<OrderConfirmedData>
{
    public async Task HandleAsync(OrderConfirmedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(context);
        if (cutoff.Since is { } since && context.OccurredAt < since)
        {
            // pedido de antes do fulfillment existir (ainda no Kafka): não vai ao fornecedor
            LogBeforeCutoff(logger, data.OrderId, context.OccurredAt, since);
            FulfillmentMetrics.SkippedBeforeCutoff();
            return;
        }
        if (await orders.GetAsync(data.OrderId, ct) is not null)
        {
            return; // o mesmo pedido de novo (evento repetido)
        }
        var items = data.Items.Select(i => new SupplierOrderItem(i.Sku, i.Name, i.Quantity)).ToList();
        orders.Add(SupplierOrder.Create(data.OrderId, data.CustomerEmail, items, supplier.Name, clock.GetUtcNow()));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "pedido {OrderId} confirmado em {ConfirmedAt}, antes do corte {Since}: não vai ao fornecedor")]
    private static partial void LogBeforeCutoff(ILogger logger, Guid orderId, DateTimeOffset confirmedAt, DateTimeOffset since);
}

/// <summary>Leva um pedido reservado pelo despachante ao fornecedor e publica o que aconteceu.</summary>
public sealed partial class SupplierOrderPlacer(ISupplierGateway supplier, IOutbox outbox, TimeProvider clock, ILogger<SupplierOrderPlacer> logger)
{
    /// <summary>Muda o pedido e agenda o evento; quem chama grava (o pedido e o outbox juntos).</summary>
    public async Task<SupplierOrderChange> PlaceAsync(SupplierOrder order, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        SupplierOrderChange change;
        try
        {
            var result = await supplier.PlaceOrderAsync(new SupplierOrderRequest(order.Id, order.CustomerEmail, order.Items), ct);
            change = result switch
            {
                SupplierPlacement.Accepted a => order.MarkPlaced(a.SupplierOrderId, clock.GetUtcNow()),
                SupplierPlacement.Rejected r => order.Fail(r.Reason, r.Detail, clock.GetUtcNow()),
                _ => throw new InvalidOperationException($"resposta desconhecida do fornecedor: {result}"),
            };
        }
        catch (SupplierUnavailableException e)
        {
            change = order.MarkAttemptFailed(e.Message, clock.GetUtcNow());
            LogUnavailable(logger, e, order.Id, order.Attempts, order.Status);
        }
        outbox.Publish(order, change);
        FulfillmentMetrics.Placement(supplier.Name, order.Status);
        return change;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "fornecedor fora ao enviar o pedido {OrderId} (tentativa {Attempts}, agora {Status})")]
    private static partial void LogUnavailable(ILogger logger, Exception e, Guid orderId, int attempts, SupplierOrderStatus status);
}

/// <summary>
/// Uma atualização do fornecedor (webhook), já com a assinatura conferida. MessageId: o fornecedor
/// repete, a loja deduplica por ele. OrderNumber: o order_id da loja. Status: CREATED, UNSHIPPED,
/// SHIPPED, DELIVERED ou CANCELLED.
/// </summary>
public sealed record SupplierUpdate(
    string MessageId, Guid OrderNumber, string? SupplierOrderId, string Status, string? TrackNumber, string? TrackingProvider, string? TrackingUrl, string? Reason);

public enum SupplierUpdateResult
{
    Applied,
    Duplicate,
    UnknownOrder,
    Ignored,
}

/// <summary>
/// Aplica o webhook do fornecedor ao pedido e publica o evento, na mesma transação do registro da
/// mensagem: o webhook repetido não publica de novo, e o atrasado não faz o pedido andar para trás.
/// </summary>
public sealed partial class SupplierUpdates(ISupplierOrderRepository orders, IOutbox outbox, TimeProvider clock, ILogger<SupplierUpdates> logger)
{
    public async Task<SupplierUpdateResult> ApplyAsync(SupplierUpdate update, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(update);
        var now = clock.GetUtcNow();
        if (!await orders.TryRecordWebhookAsync(update.MessageId, now, ct))
        {
            return SupplierUpdateResult.Duplicate;
        }
        var order = await orders.GetAsync(update.OrderNumber, ct);
        if (order is null)
        {
            // não é pedido desta loja (ou o fornecedor errou): registra e responde 200, para ele não insistir
            LogUnknownOrder(logger, update.OrderNumber, update.Status);
            await orders.SaveChangesAsync(ct);
            return SupplierUpdateResult.UnknownOrder;
        }

        var change = update.Status.ToUpperInvariant() switch
        {
            "CREATED" or "UNSHIPPED" => order.MarkPlaced(update.SupplierOrderId ?? "", now),
            "SHIPPED" when !string.IsNullOrWhiteSpace(update.TrackNumber) =>
                order.MarkShipped(new Tracking(update.TrackNumber, update.TrackingProvider, update.TrackingUrl), now),
            "DELIVERED" => order.MarkDelivered(now),
            "CANCELLED" => order.Fail(SupplierFailureReasons.SupplierCancelled, update.Reason, now),
            _ => SupplierOrderChange.None,
        };
        outbox.Publish(order, change);
        await orders.SaveChangesAsync(ct);
        return change == SupplierOrderChange.None ? SupplierUpdateResult.Ignored : SupplierUpdateResult.Applied;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "webhook do fornecedor para um pedido desconhecido {OrderId} ({Status})")]
    private static partial void LogUnknownOrder(ILogger logger, Guid orderId, string status);
}
