using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompraFacil.Fulfillment.Infrastructure.Persistence;

internal sealed class OrderPaymentFeeConfiguration : IEntityTypeConfiguration<OrderPaymentFee>
{
    public void Configure(EntityTypeBuilder<OrderPaymentFee> builder)
    {
        builder.ToTable("order_payment_fees");
        builder.HasKey(f => f.OrderId);
        builder.Property(f => f.OrderId).ValueGeneratedNever();
    }
}

internal sealed class OrderMarginRepository(FulfillmentDbContext db) : IOrderMarginRepository
{
    public Task<OrderPaymentFee?> GetFeeAsync(Guid orderId, CancellationToken ct) =>
        db.OrderPaymentFees.SingleOrDefaultAsync(f => f.OrderId == orderId, ct);

    public void AddFee(OrderPaymentFee fee) => db.OrderPaymentFees.Add(fee);

    public async Task<List<OrderEconomicsRow>> ListAsync(OrderPeriod period, int limit, CancellationToken ct)
    {
        var orders = await In(period).AsNoTracking().OrderByDescending(o => o.CreatedAt).Take(limit).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToList();
        var fees = await db.OrderPaymentFees.AsNoTracking().Where(f => ids.Contains(f.OrderId)).ToDictionaryAsync(f => f.OrderId, f => f.FeeCents, ct);
        var returned = (await db.SupplierReturns.AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct)).ToHashSet();
        return [.. orders.Select(o => new OrderEconomicsRow(o, fees.TryGetValue(o.Id, out var fee) ? fee : null, returned.Contains(o.Id)))];
    }

    // só as colunas da conta, numa consulta: a soma cobre o período inteiro, sem o limite da lista
    public Task<List<OrderEconomicsValues>> ValuesAsync(OrderPeriod period, CancellationToken ct) =>
        In(period).AsNoTracking()
            .Select(o => new OrderEconomicsValues(
                o.Status, o.RevenueCents, o.ProductCostCents, o.FreightCents,
                db.OrderPaymentFees.Where(f => f.OrderId == o.Id).Select(f => (long?)f.FeeCents).FirstOrDefault(),
                db.SupplierReturns.Any(r => r.Id == o.Id)))
            .ToListAsync(ct);

    private IQueryable<SupplierOrder> In(OrderPeriod period)
    {
        var orders = db.SupplierOrders.AsQueryable();
        if (period.From is { } from)
        {
            orders = orders.Where(o => o.CreatedAt >= from);
        }
        if (period.To is { } to)
        {
            orders = orders.Where(o => o.CreatedAt < to);
        }
        return orders;
    }
}
