using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application.SupplierOrders;

public enum SupplierOrderActionResult
{
    Done,
    NotFound,

    /// <summary>O pedido não está numa etapa em que a ação vale (já despachou, ou está sendo enviado agora).</summary>
    NotAllowed,
}

/// <summary>
/// O que a equipe faz com um pedido pelo painel (#225). Não há "reenviar" de um pedido que falhou: nessa hora o
/// cliente já foi estornado, e o pedido sairia sem pagamento. Dá para mandar tentar agora o que está na fila e
/// cancelar o que não foi despachado, e o cancelamento segue o caminho da falha: o orders cancela e o payments
/// estorna.
/// </summary>
public sealed class SupplierOrderActions(ISupplierOrderRepository orders, ISupplierGateway supplier, IOutbox outbox, TimeProvider clock)
{
    public const string CancelDetail = "cancelado pela equipe no painel";
    private static readonly TimeSpan s_keepFailed = TimeSpan.FromDays(30);

    public async Task<SupplierOrderActionResult> RetryAsync(Guid orderId, CancellationToken ct) =>
        await OptimisticConcurrency.RetryAsync(orders.Reset, async () =>
        {
            if (await orders.GetAsync(orderId, ct) is not { } order)
            {
                return SupplierOrderActionResult.NotFound;
            }
            if (!order.RetryNow())
            {
                return SupplierOrderActionResult.NotAllowed;
            }
            await orders.SaveChangesAsync(ct);
            return SupplierOrderActionResult.Done;
        });

    /// <exception cref="SupplierUnavailableException">O fornecedor não respondeu: nada muda, e a equipe tenta de novo.</exception>
    public async Task<SupplierOrderActionResult> CancelAsync(Guid orderId, CancellationToken ct) =>
        await OptimisticConcurrency.RetryAsync(orders.Reset, async () =>
        {
            if (await orders.GetAsync(orderId, ct) is not { } order)
            {
                return SupplierOrderActionResult.NotFound;
            }
            switch (order.Status)
            {
                case SupplierOrderStatus.Failed:
                    return SupplierOrderActionResult.Done; // repetido, ou o webhook do cancelamento chegou antes
                case SupplierOrderStatus.Pending or SupplierOrderStatus.Placed:
                    break;
                default:
                    return SupplierOrderActionResult.NotAllowed;
            }
            if (order.SupplierOrderId is { Length: > 0 } id && !await supplier.CancelOrderAsync(id, ct))
            {
                return SupplierOrderActionResult.NotAllowed; // saiu antes de o webhook chegar
            }
            var change = order.Fail(SupplierFailureReasons.StoreCancelled, CancelDetail, clock.GetUtcNow());
            outbox.Publish(order, change);
            await orders.SaveChangesAsync(ct);
            return SupplierOrderActionResult.Done;
        });

    /// <summary>Os que falharam nos últimos 30 dias, para o painel: o cliente já foi estornado.</summary>
    public Task<List<SupplierOrder>> ListFailedAsync(int limit, CancellationToken ct) =>
        orders.ListFailedAsync(clock.GetUtcNow() - s_keepFailed, limit, ct);
}
