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
    public void Cancel_OutOfStock_HasNoPayment()
    {
        var order = Pending();

        order.Cancel(CancellationReason.OutOfStock, s_now);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(CancellationReason.OutOfStock, order.CancellationReason);
        Assert.Null(order.PaymentFailureReason);
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
}
