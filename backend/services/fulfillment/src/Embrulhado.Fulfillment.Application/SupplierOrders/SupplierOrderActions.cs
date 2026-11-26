using Embrulhado.BuildingBlocks.Data;
using Embrulhado.BuildingBlocks.Messaging;
using Embrulhado.Fulfillment.Domain;

namespace Embrulhado.Fulfillment.Application.SupplierOrders;

public enum SupplierOrderActionResult
{
    Done,
    NotFound,

    /// <summary>O pedido não está numa etapa em que a ação vale (já despachou, ou está sendo enviado agora).</summary>
    NotAllowed,

    /// <summary>
    /// Fornecedor manual: o pedido já foi feito no fornecedor, e só a equipe pode cancelá-lo lá. O painel pede a
    /// confirmação antes de estornar o cliente.
    /// </summary>
    NeedsSupplierCancellation,
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
    /// <param name="orderId">O pedido da loja.</param>
    /// <param name="ct">Cancelamento da requisição.</param>
    /// <param name="cancelledAtSupplier">
    /// Fornecedor manual: a equipe confirma que já cancelou o pedido no site do fornecedor (sem isso, o pedido feito lá
    /// seguiria e a loja pagaria por um pedido estornado).
    /// </param>
    public async Task<SupplierOrderActionResult> CancelAsync(Guid orderId, CancellationToken ct, bool cancelledAtSupplier = false) =>
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
                // na fila da equipe (fornecedor manual), ainda não há pedido no fornecedor: só falha e estorna
                case SupplierOrderStatus.Pending or SupplierOrderStatus.AwaitingStaff or SupplierOrderStatus.Placed:
                    break;
                default:
                    return SupplierOrderActionResult.NotAllowed;
            }
            var manual = order.Supplier == ManualSupplier.ManualSupplierMode.SupplierName;
            if (manual && order.Status == SupplierOrderStatus.Placed && !cancelledAtSupplier)
            {
                return SupplierOrderActionResult.NeedsSupplierCancellation;
            }
            if (!manual && order.SupplierOrderId is { Length: > 0 } id && !await supplier.CancelOrderAsync(id, ct))
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
