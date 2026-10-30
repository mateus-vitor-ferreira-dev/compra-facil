using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Application.Invoices;
using CompraFacil.Fulfillment.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompraFacil.Fulfillment.Application.SupplierOrders;

/// <summary>A devolução foi aberta no fornecedor: o código e o endereço para o cliente mandar o produto de volta.</summary>
public sealed record SupplierReturnOpenedData(
    Guid OrderId, string CustomerEmail, string Supplier, string DisputeId, string ReturnCode, string ReturnAddress, DateTimeOffset OpenedAt);

/// <summary>O fornecedor respondeu; outcome: approved | rejected. Sem disputa, ele recusou já na abertura.</summary>
public sealed record SupplierReturnResolvedData(
    Guid OrderId, string Supplier, string? DisputeId, string Outcome, string? Reason, DateTimeOffset ResolvedAt);

public interface ISupplierReturnRepository
{
    Task<SupplierReturn?> GetAsync(Guid orderId, CancellationToken ct);

    /// <summary>Entra no SaveChanges do consumer (mesma transação do inbox e do outbox).</summary>
    void Add(SupplierReturn supplierReturn);

    /// <summary>As pendentes e abertas, e as recusadas desde <paramref name="rejectedSince"/>, sem rastrear.</summary>
    Task<List<SupplierReturn>> ListPendingAsync(DateTimeOffset rejectedSince, int limit, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>
/// OrderReturned → o cliente desistiu da compra (#223): pede a nota de devolução e põe a devolução na fila do
/// fornecedor, na transação do evento. Quem chama os serviços externos são os laços deles. Pedido que não
/// passou pelo fornecedor (de antes do fulfillment) não tem o que devolver a ele.
/// </summary>
public sealed class OnOrderReturned(
    IInvoiceRepository invoices, ISupplierOrderRepository orders, ISupplierReturnRepository returns, TimeProvider clock) : IHandleEvent<OrderReturnedData>
{
    public async Task HandleAsync(OrderReturnedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        var now = clock.GetUtcNow();
        if (await invoices.GetAsync(data.OrderId, ct) is { } invoice)
        {
            invoice.RequestReturn(now);
        }
        if (await orders.GetAsync(data.OrderId, ct) is { } order && await returns.GetAsync(data.OrderId, ct) is null)
        {
            returns.Add(SupplierReturn.Create(order, now));
        }
    }
}

/// <summary>Abre uma devolução reservada pelo laço e publica o que aconteceu; quem chama grava.</summary>
public sealed partial class SupplierReturnOpener(ISupplierGateway supplier, IOutbox outbox, TimeProvider clock, ILogger<SupplierReturnOpener> logger)
{
    public async Task OpenAsync(SupplierReturn supplierReturn, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(supplierReturn);
        var key = supplierReturn.Id.ToString();
        var now = clock.GetUtcNow();
        SupplierReturnOpening opening;
        try
        {
            opening = await supplier.OpenReturnAsync(supplierReturn.Id, supplierReturn.CustomerEmail, supplierReturn.Items, ct);
        }
        catch (SupplierUnavailableException e)
        {
            // volta à fila quando a reserva vencer; a equipe vê as tentativas no painel
            supplierReturn.RecordAttempt(e.Message);
            LogUnavailable(logger, e, supplierReturn.Id);
            return;
        }
        switch (opening)
        {
            case SupplierReturnOpening.Opened o:
                if (supplierReturn.MarkOpened(o.DisputeId, o.ReturnCode, o.ReturnAddress, now) == SupplierReturnChange.Opened)
                {
                    outbox.Add(FulfillmentEvents.Topic, key, FulfillmentEvents.SupplierReturnOpened, new SupplierReturnOpenedData(
                        supplierReturn.Id, supplierReturn.CustomerEmail, supplierReturn.Supplier, o.DisputeId, o.ReturnCode, o.ReturnAddress, now));
                }
                FulfillmentMetrics.SupplierReturn("opened");
                break;
            case SupplierReturnOpening.Refused r:
                if (supplierReturn.Resolve(approved: false, r.Reason, now) == SupplierReturnChange.Resolved)
                {
                    outbox.PublishResolved(supplierReturn);
                }
                LogRefused(logger, supplierReturn.Id, r.Reason);
                FulfillmentMetrics.SupplierReturn("refused");
                break;
            default:
                throw new InvalidOperationException("resposta do fornecedor desconhecida");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "fornecedor fora ao abrir a devolução do pedido {OrderId}; nova tentativa depois")]
    private static partial void LogUnavailable(ILogger logger, Exception e, Guid orderId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "o fornecedor recusou abrir a devolução do pedido {OrderId}: {Reason}; a equipe precisa resolver")]
    private static partial void LogRefused(ILogger logger, Guid orderId, string reason);
}

public enum SupplierReturnUpdateResult
{
    Applied,
    Duplicate,
    UnknownOrder,
    Ignored,
}

/// <summary>A resposta do fornecedor à devolução, pelo webhook: deduplicada pelo messageId, na mesma gravação.</summary>
public sealed partial class SupplierReturnUpdates(
    ISupplierReturnRepository returns, ISupplierOrderRepository webhooks, IOutbox outbox, TimeProvider clock, ILogger<SupplierReturnUpdates> logger)
{
    public async Task<SupplierReturnUpdateResult> ApplyAsync(string messageId, Guid orderId, string status, string? reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!await webhooks.TryRecordWebhookAsync(messageId, now, ct))
        {
            return SupplierReturnUpdateResult.Duplicate;
        }
        var supplierReturn = await returns.GetAsync(orderId, ct);
        if (supplierReturn is null)
        {
            LogUnknown(logger, orderId, status);
            await returns.SaveChangesAsync(ct);
            return SupplierReturnUpdateResult.UnknownOrder;
        }
        var change = status.ToUpperInvariant() switch
        {
            "APPROVED" => supplierReturn.Resolve(approved: true, null, now),
            "REJECTED" => supplierReturn.Resolve(approved: false, reason, now),
            _ => SupplierReturnChange.None, // PROCESSING: nada novo
        };
        if (change == SupplierReturnChange.Resolved)
        {
            outbox.PublishResolved(supplierReturn);
            FulfillmentMetrics.SupplierReturn(supplierReturn.Status == SupplierReturnStatus.Approved ? "approved" : "rejected");
        }
        await returns.SaveChangesAsync(ct);
        return change == SupplierReturnChange.None ? SupplierReturnUpdateResult.Ignored : SupplierReturnUpdateResult.Applied;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "webhook de devolução para um pedido sem devolução {OrderId} ({Status})")]
    private static partial void LogUnknown(ILogger logger, Guid orderId, string status);
}

/// <summary>Uma devolução em aberto e até quando o fornecedor tem para responder.</summary>
public sealed record PendingSupplierReturn(SupplierReturn Return, DateTimeOffset DueAt, bool Overdue, string? InvoiceError);

/// <summary>
/// As devoluções que pedem atenção, para o painel: as paradas além do prazo primeiro, depois as outras, as
/// mais antigas antes. As recusadas ficam por 30 dias, com o tempo de a equipe acertar com o fornecedor.
/// </summary>
public sealed class PendingSupplierReturns(
    ISupplierReturnRepository returns, IInvoiceRepository invoices, IOptions<SupplierDeadlines> deadlines, TimeProvider clock)
{
    public const int MaxLimit = 200;
    private static readonly TimeSpan s_keepRejected = TimeSpan.FromDays(30);

    public async Task<IReadOnlyList<PendingSupplierReturn>> ListAsync(int limit, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var found = await returns.ListPendingAsync(now - s_keepRejected, limit, ct);
        var errors = await invoices.ReturnErrorsAsync([.. found.Select(r => r.Id)], ct);
        return [.. found
            .Select(r =>
            {
                var due = r.RequestedAt + deadlines.Value.ReturnWithin;
                var overdue = r.Status is SupplierReturnStatus.Pending or SupplierReturnStatus.Open && due < now;
                return new PendingSupplierReturn(r, due, overdue, errors.GetValueOrDefault(r.Id));
            })
            .OrderByDescending(r => r.Overdue)
            .ThenBy(r => r.Return.RequestedAt)];
    }
}

internal static class SupplierReturnEventsExtensions
{
    public static void PublishResolved(this IOutbox outbox, SupplierReturn r) =>
        outbox.Add(FulfillmentEvents.Topic, r.Id.ToString(), FulfillmentEvents.SupplierReturnResolved, new SupplierReturnResolvedData(
            r.Id, r.Supplier, r.DisputeId, r.Status == SupplierReturnStatus.Approved ? "approved" : "rejected", r.ResolutionReason, r.ResolvedAt!.Value));
}
