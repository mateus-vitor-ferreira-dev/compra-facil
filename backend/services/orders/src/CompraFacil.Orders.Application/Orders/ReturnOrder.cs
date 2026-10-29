using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Orders.Domain;

namespace CompraFacil.Orders.Application.Orders;

/// <summary>
/// O arrependimento (#223, CDC art. 49): o dono devolve o pedido entregue em até 7 dias, sem justificar. Na
/// mesma transação, o OrderReturned vai ao outbox: o payments estorna na hora (com o frete), o fulfillment
/// abre a devolução ao fornecedor e o notifications avisa. Pedir de novo devolve o mesmo pedido, sem outro
/// evento. Fora do prazo ou antes da entrega, 409 com o motivo.
/// </summary>
public sealed class ReturnOrder(IOrderRepository orders, IOutbox outbox, TimeProvider clock)
{
    public Task<Order> HandleAsync(Guid orderId, Guid customerId, CancellationToken ct) =>
        OptimisticConcurrency.RetryAsync(orders.Reset, async () =>
        {
            var order = await orders.GetForUpdateAsync(orderId, ct);
            if (order is null || order.CustomerId != customerId)
            {
                throw new NotFoundException($"pedido {orderId} não encontrado");
            }
            bool changed;
            try
            {
                changed = order.RequestReturn(clock.GetUtcNow());
            }
            catch (DomainException e)
            {
                throw new ConflictException("O pedido não pode ser devolvido", e.Message);
            }
            if (changed)
            {
                outbox.Add(OrderEvents.Topic, order.Id.ToString(), OrderEvents.OrderReturned, OrderReturnedData.From(order));
                await orders.SaveChangesAsync(ct);
                OrdersMetrics.Progressed.Add(1, new KeyValuePair<string, object?>("step", "ReturnRequested"));
            }
            return order;
        });
}
