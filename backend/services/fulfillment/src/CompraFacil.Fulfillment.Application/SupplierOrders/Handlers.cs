using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Application.Invoices;
using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Domain;
using Microsoft.Extensions.Logging;

namespace CompraFacil.Fulfillment.Application.SupplierOrders;

/// <summary>
/// O pedido pago entra na fila do fornecedor, com a nota fiscal pendente (#221), na transação do evento. Quem
/// emite a nota e chama o fornecedor é o despachante (<see cref="SupplierOrderPlacer"/>): chamar daqui
/// prenderia o consumer num serviço externo, e uma queda depois da chamada repetiria o evento sem saber se
/// o pedido foi. Pedido sem endereço (de antes do #221) vai sem nota.
/// </summary>
public sealed partial class OnOrderConfirmed(
    ISupplierOrderRepository orders, IInvoiceRepository invoices, ISupplierProductRepository products, InvoiceSettings invoicing,
    ISupplierGateway supplier, OrderCutoff cutoff, TimeProvider clock, ILogger<OnOrderConfirmed> logger) : IHandleEvent<OrderConfirmedData>
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
        var now = clock.GetUtcNow();
        var items = data.Items.Select(i => new SupplierOrderItem(i.Sku, i.Name, i.Quantity)).ToList();
        orders.Add(SupplierOrder.Create(data.OrderId, data.CustomerEmail, items, supplier.Name, now, data.ShipTo, data.Shipping?.PromisedBy));
        if (invoicing.Enabled && data.ShipTo is { } shipTo)
        {
            // o NCM sai da categoria do produto no fornecedor (o SKU da loja é o vid dele)
            var categories = (await products.GetManyAsync(supplier.Name, [.. data.Items.Select(i => i.Sku)], ct))
                .ToDictionary(p => p.Vid, p => p.Category);
            var lines = data.Items.Select(i => new InvoiceLine(
                i.Sku, i.Name, invoicing.NcmFor(categories.GetValueOrDefault(i.Sku)), i.Quantity,
                i.UnitPrice.AmountCents, i.LineTotal.AmountCents)).ToList();
            invoices.Add(Invoice.Create(data.OrderId, data.CustomerId, data.CustomerEmail, shipTo, lines, data.PaymentMethod ?? "pix", now,
                data.Shipping?.Price.AmountCents ?? 0));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "pedido {OrderId} confirmado em {ConfirmedAt}, antes do corte {Since}: não vai ao fornecedor")]
    private static partial void LogBeforeCutoff(ILogger logger, Guid orderId, DateTimeOffset confirmedAt, DateTimeOffset since);
}

/// <summary>
/// Leva um pedido reservado pelo despachante ao fornecedor e publica o que aconteceu. Com nota (#221), emite
/// antes: autorizada, o pedido vai com a chave e o XML; rejeitada, o pedido não vai (falha com
/// invoice_rejected, e o orders cancela e estorna); emissor fora do ar, o pedido volta para a fila.
/// </summary>
public sealed partial class SupplierOrderPlacer(
    ISupplierGateway supplier, IInvoiceIssuer issuer, IOutbox outbox, TimeProvider clock, ILogger<SupplierOrderPlacer> logger)
{
    /// <summary>Muda o pedido (e a nota) e agenda os eventos; quem chama grava tudo junto com o outbox.</summary>
    public async Task<SupplierOrderChange> PlaceAsync(SupplierOrder order, Invoice? invoice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        SupplierOrderChange change;
        try
        {
            var document = await InvoiceForAsync(order, invoice, ct);
            if (document is null && invoice is not null)
            {
                change = order.Fail(SupplierFailureReasons.InvoiceRejected,
                    $"nota rejeitada pela SEFAZ ({invoice.RejectionCode}): {invoice.RejectionReason}", clock.GetUtcNow());
            }
            else
            {
                var result = await supplier.PlaceOrderAsync(new SupplierOrderRequest(
                    order.Id, order.CustomerEmail, order.Items, order.ShipTo, document?.AccessKey, document?.Xml), ct);
                change = result switch
                {
                    SupplierPlacement.Accepted a => order.MarkPlaced(a.SupplierOrderId, clock.GetUtcNow()),
                    SupplierPlacement.Rejected r => order.Fail(r.Reason, r.Detail, clock.GetUtcNow()),
                    _ => throw new InvalidOperationException($"resposta desconhecida do fornecedor: {result}"),
                };
            }
        }
        catch (SupplierUnavailableException e)
        {
            change = order.MarkAttemptFailed(e.Message, clock.GetUtcNow());
            LogUnavailable(logger, e, order.Id, order.Attempts, order.Status);
        }
        catch (InvoiceIssuerUnavailableException e)
        {
            change = order.MarkAttemptFailed($"emissor de nota: {e.Message}", clock.GetUtcNow());
            LogIssuerUnavailable(logger, e, order.Id, order.Attempts, order.Status);
        }
        outbox.Publish(order, change);
        FulfillmentMetrics.Placement(supplier.Name, order.Status);
        return change;
    }

    /// <summary>
    /// A nota que vai com o pedido: emite a pendente e busca o XML da autorizada. Nulo sem nota (pedido de
    /// antes do #221) ou com ela rejeitada (o chamador distingue pela nota).
    /// </summary>
    private async Task<InvoiceDocument?> InvoiceForAsync(SupplierOrder order, Invoice? invoice, CancellationToken ct)
    {
        if (invoice is null)
        {
            return null;
        }
        if (invoice.Status == InvoiceStatus.Pending)
        {
            var issued = await issuer.IssueAsync(invoice, ct);
            var change = issued switch
            {
                InvoiceIssue.Authorized a => invoice.MarkAuthorized(a.ProviderId, a.AccessKey, a.Number, a.Series, a.IssuedAt),
                InvoiceIssue.Rejected r => invoice.MarkRejected(r.ProviderId, r.Code, r.Reason, clock.GetUtcNow()),
                _ => throw new InvalidOperationException($"resposta desconhecida do emissor: {issued}"),
            };
            outbox.Publish(invoice, change);
            FulfillmentMetrics.Invoice(invoice.Status == InvoiceStatus.Authorized ? "authorized" : "rejected");
            if (invoice.Status == InvoiceStatus.Rejected)
            {
                LogRejected(logger, order.Id, invoice.RejectionCode, invoice.RejectionReason);
            }
        }
        return invoice.Status == InvoiceStatus.Authorized
            ? new InvoiceDocument(invoice.AccessKey!, await issuer.GetXmlAsync(invoice.ProviderId!, ct))
            : null;
    }

    private sealed record InvoiceDocument(string AccessKey, string Xml);

    [LoggerMessage(Level = LogLevel.Warning, Message = "fornecedor fora ao enviar o pedido {OrderId} (tentativa {Attempts}, agora {Status})")]
    private static partial void LogUnavailable(ILogger logger, Exception e, Guid orderId, int attempts, SupplierOrderStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "emissor de nota fora ao emitir a do pedido {OrderId} (tentativa {Attempts}, agora {Status})")]
    private static partial void LogIssuerUnavailable(ILogger logger, Exception e, Guid orderId, int attempts, SupplierOrderStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "nota do pedido {OrderId} rejeitada pela SEFAZ ({Code}): {Reason}; o pedido não vai ao fornecedor")]
    private static partial void LogRejected(ILogger logger, Guid orderId, int? code, string? reason);
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
