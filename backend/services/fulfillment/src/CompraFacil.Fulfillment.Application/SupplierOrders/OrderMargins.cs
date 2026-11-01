using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Application.Invoices;
using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application.SupplierOrders;

/// <summary>data do PaymentApproved (só os campos usados): a taxa do provedor neste pagamento.</summary>
public sealed record PaymentApprovedData(Guid OrderId, long FeeCents);

/// <summary>Um pedido com a taxa do pagamento e se o cliente devolveu.</summary>
public sealed record OrderEconomicsRow(SupplierOrder Order, long? FeeCents, bool Returned);

public interface IOrderMarginRepository
{
    Task<OrderPaymentFee?> GetFeeAsync(Guid orderId, CancellationToken ct);

    /// <summary>Entra no SaveChanges do consumer.</summary>
    void AddFee(OrderPaymentFee fee);

    /// <summary>Os pedidos mais recentes, sem rastrear.</summary>
    Task<List<OrderEconomicsRow>> ListAsync(int limit, CancellationToken ct);
}

/// <summary>PaymentApproved → guarda a taxa do provedor. O cartão aprova na autorização e de novo na captura, com a mesma taxa.</summary>
public sealed class OnPaymentApproved(IOrderMarginRepository margins) : IHandleEvent<PaymentApprovedData>
{
    public async Task HandleAsync(PaymentApprovedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (await margins.GetFeeAsync(data.OrderId, ct) is null)
        {
            margins.AddFee(OrderPaymentFee.Create(data.OrderId, data.FeeCents));
        }
    }
}

public enum OrderOutcome
{
    InProgress,
    Delivered,
    Cancelled,
    Returned,
}

/// <summary>A conta de um pedido; a margem é nula se falta um valor, ou se o cliente foi estornado.</summary>
public sealed record OrderMargin(
    SupplierOrder Order, OrderOutcome Outcome, long? RevenueCents, long? ProductCostCents, long? FreightCents, long? FeeCents, long? MarginCents);

/// <summary>
/// A margem real por pedido (#225): o que o cliente pagou, menos o custo dos produtos, o frete repassado ao
/// fornecedor e a taxa do pagamento. O pedido estornado não entra na conta: o quanto a loja perde nele depende
/// do que o fornecedor devolver.
/// </summary>
public sealed class OrderMargins(IOrderMarginRepository margins)
{
    public const int MaxLimit = 200;

    public async Task<IReadOnlyList<OrderMargin>> ListAsync(int limit, CancellationToken ct) =>
        [.. (await margins.ListAsync(limit, ct)).Select(From)];

    private static OrderMargin From(OrderEconomicsRow row)
    {
        var o = row.Order;
        var outcome = row.Returned ? OrderOutcome.Returned : o.Status switch
        {
            SupplierOrderStatus.Failed => OrderOutcome.Cancelled,
            SupplierOrderStatus.Delivered => OrderOutcome.Delivered,
            _ => OrderOutcome.InProgress,
        };
        long? margin = outcome is OrderOutcome.Cancelled or OrderOutcome.Returned
            ? null
            : o.RevenueCents - o.ProductCostCents - o.FreightCents - row.FeeCents; // nulo se faltar qualquer um
        return new OrderMargin(o, outcome, o.RevenueCents, o.ProductCostCents, o.FreightCents, row.FeeCents, margin);
    }
}
