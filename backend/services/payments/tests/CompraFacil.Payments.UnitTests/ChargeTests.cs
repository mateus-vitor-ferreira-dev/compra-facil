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
        stockFirst.RecordOrder(Guid.NewGuid(), "ana@exemplo.com", s_amount, PaymentInstruction.Pix, s_now);
        Assert.True(stockFirst.ReadyToCharge);

        var orderFirst = Charge.For(Guid.NewGuid(), s_now);
        orderFirst.RecordOrder(Guid.NewGuid(), "ana@exemplo.com", s_amount, PaymentInstruction.Pix, s_now);
        Assert.False(orderFirst.ReadyToCharge);
        orderFirst.RecordStockReserved(s_now);
        Assert.True(orderFirst.ReadyToCharge);
    }

    [Fact]
    public void CancelledOrCharged_IsNotChargedAgain()
    {
        var cancelled = Charge.For(Guid.NewGuid(), s_now);
        cancelled.RecordOrder(Guid.NewGuid(), "ana@exemplo.com", s_amount, PaymentInstruction.Pix, s_now);
        cancelled.Cancel(s_now);
        cancelled.RecordStockReserved(s_now);
        Assert.False(cancelled.ReadyToCharge);
        Assert.Throws<DomainException>(() => cancelled.AttachPayment(Guid.NewGuid(), s_now));

        var charged = Charge.For(Guid.NewGuid(), s_now);
        charged.RecordOrder(Guid.NewGuid(), "ana@exemplo.com", s_amount, PaymentInstruction.Pix, s_now);
        charged.RecordStockReserved(s_now);
        charged.AttachPayment(Guid.NewGuid(), s_now);
        Assert.False(charged.ReadyToCharge);
        Assert.Throws<DomainException>(() => charged.AttachPayment(Guid.NewGuid(), s_now));
    }

    [Fact]
    public void RecordOrder_RejectsNonPositiveAmount() =>
        Assert.Throws<DomainException>(() => Charge.For(Guid.NewGuid(), s_now).RecordOrder(Guid.NewGuid(), null, new Money(0, "BRL"), PaymentInstruction.Pix, s_now));

    [Fact]
    public void Card_NeedsTokenAndBrand_AndTheTokenIsDroppedOnCharge()
    {
        var charge = Charge.For(Guid.NewGuid(), s_now);
        Assert.Throws<DomainException>(() => charge.RecordOrder(Guid.NewGuid(), null, s_amount, new PaymentInstruction(PaymentMethod.Card, 1, null, "visa"), s_now));
        charge.RecordOrder(Guid.NewGuid(), null, s_amount, new PaymentInstruction(PaymentMethod.Card, 30, "tok", "visa"), s_now);
        Assert.Equal(12, charge.Installments); // até 12x
        charge.RecordStockReserved(s_now);
        charge.AttachPayment(Guid.NewGuid(), s_now);
        Assert.Null(charge.CardToken);
    }

    private static Payment Start(PaymentMethod method = PaymentMethod.Card, int installments = 1) =>
        Payment.Start(Guid.NewGuid(), Guid.NewGuid(), s_amount, method, installments, "mercadopago", s_now);

    private static readonly PixCharge s_pix = new("000201", "png", null, s_now.AddMinutes(30));

    [Fact]
    public void Pix_PendingThenPaidOrExpired()
    {
        Assert.Throws<DomainException>(() => Start(PaymentMethod.Pix, installments: 2));
        var paid = Start(PaymentMethod.Pix);
        paid.MarkPending("p1", s_pix, s_now);
        Assert.True(paid.IsOpen);
        paid.Approve("p1", 198, s_now);
        Assert.Equal((PaymentStatus.Approved, 198L), (paid.Status, paid.FeeCents));
        Assert.Throws<DomainException>(() => paid.Fail(FailureReason.PixExpired, null, s_now));

        var expired = Start(PaymentMethod.Pix);
        expired.MarkPending("p2", s_pix, s_now);
        expired.Fail(FailureReason.PixExpired, null, s_now);
        Assert.Equal(FailureReason.PixExpired, expired.FailureReason);
    }

    [Fact]
    public void Card_AuthorizedThenCapturedOrVoided()
    {
        var captured = Start();
        captured.Authorize("c1", "5682", 995, s_now);
        Assert.True(captured.IsPaid);
        Assert.Throws<DomainException>(() => captured.Refund(100, "k", "r", s_now)); // autorizado se cancela, não se estorna
        captured.Capture(s_now);
        Assert.Equal(PaymentStatus.Approved, captured.Status);

        var voided = Start();
        voided.Authorize("c2", "5682", 995, s_now);
        voided.Void(s_now);
        Assert.Equal(PaymentStatus.Voided, voided.Status);
        Assert.Throws<DomainException>(() => voided.Capture(s_now));
    }

    [Fact]
    public void Refund_InParts_UpToTheTotal()
    {
        var p = Start();
        p.Authorize("c", null, 0, s_now);
        p.Capture(s_now);
        p.Refund(5000, "k1", "r1", s_now);
        Assert.Equal((PaymentStatus.Approved, 5000L), (p.Status, p.RefundedCents));
        Assert.Throws<DomainException>(() => p.Refund(s_amount.AmountCents, "k2", "r2", s_now));
        p.Refund(s_amount.AmountCents - 5000, "k3", "r3", s_now);
        Assert.Equal(PaymentStatus.Refunded, p.Status);
    }
}
