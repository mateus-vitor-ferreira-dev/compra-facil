using CompraFacil.Orders.Domain;

namespace CompraFacil.Orders.UnitTests;

public sealed class OrderTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static OrderItem Item(int quantity = 2, long cents = 1990, Guid? productId = null) =>
        new(productId ?? Guid.NewGuid(), "SKU-1", "Fone", quantity, new Money(cents, "BRL"), CampaignId: null);

    private static Order Pending(params OrderItem[] items) =>
        Order.Place(Guid.NewGuid(), Guid.NewGuid(), "ana@exemplo.com", items.Length == 0 ? [Item()] : items, "chave-123", "hash", s_now);

    [Fact]
    public void Place_StartsPending_WithTotalOfLines()
    {
        var order = Pending(Item(quantity: 2, cents: 1990), Item(quantity: 1, cents: 5000));

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(new Money(8980, "BRL"), order.Total);
        Assert.Equal(new Money(3980, "BRL"), order.Items[0].LineTotal);
        Assert.Null(order.CancellationReason);
        Assert.Equal(s_now, order.UpdatedAt);
    }

    [Fact]
    public void Place_RejectsEmptyTooManyAndRepeated()
    {
        var p = Guid.NewGuid();
        Assert.Throws<DomainException>(() => Order.Place(Guid.NewGuid(), Guid.NewGuid(), "a@b.c", [], "k", "h", s_now));
        Assert.Throws<DomainException>(() => Pending([.. Enumerable.Range(0, Order.MaxItems + 1).Select(_ => Item())]));
        Assert.Throws<DomainException>(() => Pending(Item(productId: p), Item(productId: p)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Place_RejectsQuantityOutOfRange(int quantity) =>
        Assert.Throws<DomainException>(() => Pending(Item(quantity: quantity)));

    [Fact]
    public void Place_RejectsMixedCurrencies()
    {
        var real = Item();
        var dollar = Item() with { UnitPrice = new Money(100, "USD") };
        Assert.Throws<DomainException>(() => Pending(real, dollar));
    }

    [Fact]
    public void Confirm_FromPending()
    {
        var order = Pending();
        var payment = Guid.NewGuid();

        order.Confirm(payment, s_now.AddMinutes(1));

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(payment, order.PaymentId);
        Assert.Equal(s_now.AddMinutes(1), order.UpdatedAt);
    }

    [Fact]
    public void MarkStockReserved_MovesToTheChargingStep_Once()
    {
        var order = Pending();
        Assert.Null(order.StockReservedAt);

        Assert.True(order.MarkStockReserved(s_now.AddSeconds(1)));
        Assert.False(order.MarkStockReserved(s_now.AddSeconds(2))); // evento repetido

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(s_now.AddSeconds(1), order.StockReservedAt);
        Assert.Equal(s_now.AddSeconds(1), order.UpdatedAt);
    }

    [Fact]
    public void MarkStockReserved_AfterTheEnd_ChangesNothing()
    {
        var order = Pending();
        order.Cancel(CancellationReason.OutOfStock, s_now);

        Assert.False(order.MarkStockReserved(s_now.AddSeconds(1)));
        Assert.Null(order.StockReservedAt);
        Assert.Equal(s_now, order.UpdatedAt);
    }

    [Fact]
    public void PaymentResult_ImpliesTheReservation_EvenIfStockReservedComesLater()
    {
        var confirmed = Pending();
        confirmed.Confirm(Guid.NewGuid(), s_now.AddSeconds(3));
        var refused = Pending();
        refused.Cancel(CancellationReason.PaymentFailed, s_now.AddSeconds(4), Guid.NewGuid(), "card_declined");
        var reserved = Pending();
        reserved.MarkStockReserved(s_now.AddSeconds(1));
        reserved.Confirm(Guid.NewGuid(), s_now.AddSeconds(5));

        Assert.Equal(s_now.AddSeconds(3), confirmed.StockReservedAt);
        Assert.Equal(s_now.AddSeconds(4), refused.StockReservedAt);
        Assert.Equal(s_now.AddSeconds(1), reserved.StockReservedAt); // a reserva de verdade é mantida
    }

    [Fact]
    public void Cancel_OutOfStock_HasNoPayment()
    {
        var order = Pending();

        order.Cancel(CancellationReason.OutOfStock, s_now);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(CancellationReason.OutOfStock, order.CancellationReason);
        Assert.Null(order.PaymentFailureReason);
        Assert.Null(order.StockReservedAt);
    }

    [Fact]
    public void Cancel_PaymentFailed_RequiresTheReason()
    {
        var order = Pending();
        Assert.Throws<DomainException>(() => order.Cancel(CancellationReason.PaymentFailed, s_now));

        var payment = Guid.NewGuid();
        order.Cancel(CancellationReason.PaymentFailed, s_now, payment, "card_declined");

        Assert.Equal("card_declined", order.PaymentFailureReason);
        Assert.Equal(payment, order.PaymentId);
    }

    [Fact]
    public void FinalStates_DoNotChange()
    {
        var confirmed = Pending();
        confirmed.Confirm(Guid.NewGuid(), s_now);
        var cancelled = Pending();
        cancelled.Cancel(CancellationReason.OutOfStock, s_now);

        Assert.Throws<DomainException>(() => confirmed.Cancel(CancellationReason.OutOfStock, s_now));
        Assert.Throws<DomainException>(() => confirmed.Confirm(Guid.NewGuid(), s_now));
        Assert.Throws<DomainException>(() => cancelled.Confirm(Guid.NewGuid(), s_now));
        Assert.Equal(OrderStatus.Confirmed, confirmed.Status);
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
    }

    private static Order Paid()
    {
        var order = Pending();
        order.Confirm(Guid.NewGuid(), s_now.AddMinutes(1));
        return order;
    }

    private static readonly Tracking s_tracking = new("BR1", "Correios", null);

    [Fact]
    public void Supplier_TakesThePaidOrderToDelivered()
    {
        var order = Paid();

        Assert.True(order.MarkSentToSupplier(s_now.AddMinutes(2)));
        Assert.False(order.MarkSentToSupplier(s_now.AddMinutes(3)));
        Assert.True(order.Ship(s_tracking, s_now.AddDays(1)));
        Assert.True(order.Deliver(s_now.AddDays(3)));

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal((s_now.AddMinutes(2), s_now.AddDays(1), s_now.AddDays(3)), (order.SentToSupplierAt, order.ShippedAt, order.DeliveredAt));
        Assert.Equal(s_tracking, order.Tracking);
    }

    [Fact]
    public void Supplier_DeliveredWithoutShipping_FillsTheSteps()
    {
        var order = Paid();

        Assert.True(order.Deliver(s_now.AddDays(2)));

        Assert.Equal((s_now.AddDays(2), s_now.AddDays(2)), (order.SentToSupplierAt, order.ShippedAt));
    }

    [Fact]
    public void Supplier_IgnoresPendingAndFinishedOrders()
    {
        var pending = Pending();
        Assert.False(pending.MarkSentToSupplier(s_now));
        Assert.False(pending.Ship(s_tracking, s_now));
        Assert.False(pending.Deliver(s_now));
        Assert.False(pending.FailAtSupplier(s_now));

        var delivered = Paid();
        delivered.Deliver(s_now);
        Assert.False(delivered.Ship(s_tracking, s_now));
        Assert.False(delivered.FailAtSupplier(s_now));
        Assert.Equal(OrderStatus.Delivered, delivered.Status);
    }

    [Fact]
    public void SupplierFailure_CancelsThePaidOrder_UntilRefunded()
    {
        var order = Paid();

        Assert.True(order.FailAtSupplier(s_now.AddHours(1)));
        Assert.Equal((OrderStatus.Cancelled, CancellationReason.SupplierFailed), (order.Status, order.CancellationReason));
        Assert.True(order.AwaitingRefund);

        Assert.True(order.MarkRefunded(s_now.AddHours(2)));
        Assert.False(order.MarkRefunded(s_now.AddHours(3)));
        Assert.Equal(s_now.AddHours(2), order.RefundedAt);
        Assert.False(order.AwaitingRefund);
    }

    [Fact]
    public void SupplierFailure_AfterShipping_IsIgnored()
    {
        var order = Paid();
        order.Ship(s_tracking, s_now);

        Assert.False(order.FailAtSupplier(s_now));
        Assert.Equal(OrderStatus.Shipped, order.Status);
    }

    [Fact]
    public void PendingOrder_CannotBeCancelledBySupplier() =>
        Assert.Throws<DomainException>(() => Pending().Cancel(CancellationReason.SupplierFailed, s_now));

    [Fact]
    public void Refund_OnlyForTheOrderCancelledAfterPayment()
    {
        var order = Pending();
        order.Cancel(CancellationReason.OutOfStock, s_now);

        Assert.False(order.MarkRefunded(s_now));
        Assert.Null(order.RefundedAt);
    }
}
