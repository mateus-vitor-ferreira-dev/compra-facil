using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Payments.Domain;

namespace CompraFacil.Payments.Application.Payments;

/// <summary>
/// Registra um fato sobre o pedido e cobra se já tiver tudo (valor + estoque garantido). Usado pelos
/// três handlers: a cobrança acontece no evento que chegar por último, em qualquer ordem.
/// </summary>
public sealed class ChargeProcessor(IPaymentRepository payments, IPaymentGateway gateway, IOutbox outbox, TimeProvider clock)
{
    public Task ApplyAsync(Guid orderId, Action<Charge, DateTimeOffset> record, CancellationToken ct) =>
        OptimisticConcurrency.RetryAsync(payments.Reset, async () =>
        {
            var now = clock.GetUtcNow();
            var charge = await payments.GetChargeAsync(orderId, ct);
            if (charge is null)
            {
                charge = Charge.For(orderId, now);
                payments.Add(charge);
            }
            record(charge, now);
            Payment? payment = null;
            if (charge.ReadyToCharge)
            {
                payment = await ChargeAsync(charge, now, ct);
            }
            await payments.SaveChangesAsync(ct);
            if (payment is not null)
            {
                PaymentsMetrics.Charges.Add(1, new KeyValuePair<string, object?>("result", payment.Status.ToString()), new KeyValuePair<string, object?>("reason", payment.FailureReason?.ToString() ?? "-"));
            }
            return true;
        });

    private async Task<Payment> ChargeAsync(Charge charge, DateTimeOffset now, CancellationToken ct)
    {
        var amount = charge.Amount!.Value;
        var payment = Payment.Start(Guid.CreateVersion7(), charge.OrderId, amount, gateway.Provider, now);
        charge.AttachPayment(payment.Id, now);
        payments.Add(payment);

        // falha temporária sobe: nada é gravado e a mensagem é tentada de novo, com a mesma chave
        var result = await gateway.ChargeAsync(
            new ChargeRequest(charge.OrderId.ToString(), charge.OrderId, charge.CustomerId!.Value, amount), ct);

        var key = charge.OrderId.ToString();
        switch (result)
        {
            case ChargeApproved approved:
                payment.Approve(approved.ProviderReference, clock.GetUtcNow());
                outbox.Add(PaymentEvents.Topic, key, PaymentEvents.PaymentApproved,
                    new PaymentApprovedData(charge.OrderId, payment.Id, amount, payment.Provider, payment.ProviderReference));
                break;
            case ChargeDeclined declined:
                payment.Fail(declined.Reason, declined.ProviderReference, clock.GetUtcNow());
                outbox.Add(PaymentEvents.Topic, key, PaymentEvents.PaymentFailed,
                    new PaymentFailedData(charge.OrderId, payment.Id, amount, payment.Provider, declined.Reason));
                break;
            default:
                throw new InvalidOperationException($"resposta do gateway desconhecida: {result}");
        }
        return payment;
    }
}

/// <summary>OrderPlaced → guarda valor e cliente (e cobra se o estoque já foi reservado).</summary>
public sealed class OnOrderPlaced(ChargeProcessor processor) : IHandleEvent<OrderPlacedData>
{
    public Task HandleAsync(OrderPlacedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return processor.ApplyAsync(data.OrderId, (charge, now) =>
        {
            if (charge.Amount is null)
            {
                charge.RecordOrder(data.CustomerId, data.Total, now);
            }
        }, ct);
    }
}

/// <summary>StockReserved → estoque garantido (e cobra se o OrderPlaced já chegou).</summary>
public sealed class OnStockReserved(ChargeProcessor processor) : IHandleEvent<StockReservedData>
{
    public Task HandleAsync(StockReservedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return processor.ApplyAsync(data.OrderId, (charge, now) => charge.RecordStockReserved(now), ct);
    }
}

/// <summary>OrderCancelled → descarta a cobrança que ainda não aconteceu (reserva recusada).</summary>
public sealed class OnOrderCancelled(ChargeProcessor processor) : IHandleEvent<OrderCancelledData>
{
    public Task HandleAsync(OrderCancelledData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        return processor.ApplyAsync(data.OrderId, (charge, now) => charge.Cancel(now), ct);
    }
}
