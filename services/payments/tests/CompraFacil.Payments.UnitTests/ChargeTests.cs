using CompraFacil.Payments.Domain;

namespace CompraFacil.Payments.UnitTests;

public sealed class ChargeTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly Money s_amount = new(19990, "BRL");

    [Fact]
    public void ReadyOnlyWithOrderAndStock_InAnyOrder()
    {
        var stockFirst = Charge.For(Guid.NewGuid(), s_now);
        stockFirst.RecordStockReserved(s_now);
        Assert.False(stockFirst.ReadyToCharge);
        stockFirst.RecordOrder(Guid.NewGuid(), s_amount, s_now);
        Assert.True(stockFirst.ReadyToCharge);

        var orderFirst = Charge.For(Guid.NewGuid(), s_now);
        orderFirst.RecordOrder(Guid.NewGuid(), s_amount, s_now);
        Assert.False(orderFirst.ReadyToCharge);
        orderFirst.RecordStockReserved(s_now);
        Assert.True(orderFirst.ReadyToCharge);
    }

    [Fact]
    public void CancelledOrCharged_IsNotChargedAgain()
    {
        var cancelled = Charge.For(Guid.NewGuid(), s_now);
        cancelled.RecordOrder(Guid.NewGuid(), s_amount, s_now);
        cancelled.Cancel(s_now);
        cancelled.RecordStockReserved(s_now);
        Assert.False(cancelled.ReadyToCharge);
        Assert.Throws<DomainException>(() => cancelled.AttachPayment(Guid.NewGuid(), s_now));

        var charged = Charge.For(Guid.NewGuid(), s_now);
        charged.RecordOrder(Guid.NewGuid(), s_amount, s_now);
        charged.RecordStockReserved(s_now);
        charged.AttachPayment(Guid.NewGuid(), s_now);
        Assert.False(charged.ReadyToCharge);
        Assert.Throws<DomainException>(() => charged.AttachPayment(Guid.NewGuid(), s_now));
    }

    [Fact]
    public void RecordOrder_RejectsNonPositiveAmount() =>
        Assert.Throws<DomainException>(() => Charge.For(Guid.NewGuid(), s_now).RecordOrder(Guid.NewGuid(), new Money(0, "BRL"), s_now));

    [Fact]
    public void Payment_EndsApprovedOrFailed_Once()
    {
        var approved = Payment.Start(Guid.NewGuid(), Guid.NewGuid(), s_amount, "fake", s_now);
        approved.Approve("fake_ch_1", s_now);
        Assert.Equal(PaymentStatus.Approved, approved.Status);
        Assert.Throws<DomainException>(() => approved.Fail(FailureReason.CardDeclined, null, s_now));

        var failed = Payment.Start(Guid.NewGuid(), Guid.NewGuid(), s_amount, "fake", s_now);
        failed.Fail(FailureReason.InsufficientFunds, "fake_ch_2", s_now);
        Assert.Equal((PaymentStatus.Failed, FailureReason.InsufficientFunds), (failed.Status, failed.FailureReason));
        Assert.Throws<DomainException>(() => failed.Approve("x", s_now));
    }
}
