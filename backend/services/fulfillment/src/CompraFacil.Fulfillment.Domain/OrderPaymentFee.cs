namespace CompraFacil.Fulfillment.Domain;

/// <summary>
/// A taxa do provedor de pagamento de um pedido (#225), para a margem. Fica à parte do pedido no fornecedor: o
/// PaymentApproved chega antes do OrderConfirmed, quando esse pedido ainda não existe aqui.
/// </summary>
public sealed class OrderPaymentFee
{
    private OrderPaymentFee()
    {
        // EF Core
    }

    public Guid OrderId { get; private set; }
    public long FeeCents { get; private set; }

    public static OrderPaymentFee Create(Guid orderId, long feeCents) => new() { OrderId = orderId, FeeCents = feeCents };
}
