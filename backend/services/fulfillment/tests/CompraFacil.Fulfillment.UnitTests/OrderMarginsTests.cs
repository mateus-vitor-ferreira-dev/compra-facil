using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.UnitTests;

public sealed class OrderMarginsTests
{
    private sealed class FakeMargins(List<OrderEconomicsValues> values) : IOrderMarginRepository
    {
        public Task<OrderPaymentFee?> GetFeeAsync(Guid orderId, CancellationToken ct) => Task.FromResult<OrderPaymentFee?>(null);

        public void AddFee(OrderPaymentFee fee)
        {
        }

        public Task<List<OrderEconomicsRow>> ListAsync(OrderPeriod period, int limit, CancellationToken ct) => Task.FromResult(new List<OrderEconomicsRow>());

        public Task<List<OrderEconomicsValues>> ValuesAsync(OrderPeriod period, CancellationToken ct) => Task.FromResult(values);
    }

    [Fact]
    public async Task Soma_so_os_pedidos_com_a_conta_completa_e_conta_os_outros_a_parte()
    {
        var values = new List<OrderEconomicsValues>
        {
            new(SupplierOrderStatus.Delivered, 10_000, 4_000, 1_500, 300, Returned: false), // margem 4.200
            new(SupplierOrderStatus.Placed, 5_000, 4_000, 1_500, 200, Returned: false), // em andamento, margem -700
            new(SupplierOrderStatus.Delivered, 8_000, null, 1_000, 250, Returned: false), // sem custo: incompleto
            new(SupplierOrderStatus.Failed, 7_000, 3_000, 1_000, 200, Returned: false), // cancelado: estornado
            new(SupplierOrderStatus.Delivered, 9_000, 3_000, 1_000, 200, Returned: true), // devolvido: estornado
        };

        var totals = (await new OrderMargins(new FakeMargins(values)).ReportAsync(new OrderPeriod(null, null), 50, CancellationToken.None)).Totals;

        Assert.Equal(new OrderMarginTotals(
            Orders: 5, Counted: 2, Refunded: 2, Incomplete: 1,
            RevenueCents: 15_000, ProductCostCents: 8_000, FreightCents: 3_000, FeeCents: 500, MarginCents: 3_500), totals);
    }
}
