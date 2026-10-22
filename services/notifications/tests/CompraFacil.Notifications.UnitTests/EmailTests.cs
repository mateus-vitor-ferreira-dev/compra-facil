using CompraFacil.Notifications.Application.Emails;
using CompraFacil.Notifications.Domain;

namespace CompraFacil.Notifications.UnitTests;

public sealed class EmailTests
{
    [Theory]
    [InlineData(0, "R$ 0,00")]
    [InlineData(5, "R$ 0,05")]
    [InlineData(218390, "R$ 2.183,90")]
    [InlineData(5568507, "R$ 55.685,07")]
    [InlineData(123456789012, "R$ 1.234.567.890,12")]
    public void Brl_FormatsLikeTheStorefront(long cents, string expected) =>
        Assert.Equal(expected, EmailComposer.Brl(new Money(cents, "BRL")));

    [Theory]
    [InlineData("out_of_stock", null, "sem estoque")]
    [InlineData("payment_failed", "insufficient_funds", "limite insuficiente")]
    [InlineData("payment_failed", "card_declined", "cartão foi recusado")]
    [InlineData("payment_failed", "fraud_suspected", "não foi aprovado")] // sem expor a suspeita de fraude
    public void Cancellation_IsExplainedInCustomerLanguage(string reason, string? payment, string expected) =>
        Assert.Contains(expected, EmailComposer.Explain(reason, payment).Reason, StringComparison.Ordinal);

    [Fact]
    public async Task Confirmed_ListsItemsAndTotal()
    {
        var order = Guid.Parse("01a16430-0000-7000-8000-00000000abcd");
        var email = await EmailComposer.ConfirmedAsync(new OrderConfirmedData(order, "ana@exemplo.com",
            [new OrderItem("Fone <Pro> Elétrico", 2, new Money(19990, "BRL"), new Money(39980, "BRL"))], new Money(39980, "BRL")));

        Assert.Equal("Pedido 0000ABCD confirmado", email.Subject);
        Assert.Contains("Fone &lt;Pro&gt; Elétrico", email.HtmlBody, StringComparison.Ordinal); // escapado, acento legível
        Assert.Contains("Fone <Pro> Elétrico", email.TextBody, StringComparison.Ordinal);       // texto puro: como é
        Assert.Contains("R$ 399,80", email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<td>", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Notification_RetriesThenGivesUp()
    {
        var n = Notification.Create(Guid.NewGuid(), Guid.NewGuid(), "OrderConfirmed", "a@b.c", "s", "<p>h</p>", "t", DateTimeOffset.UtcNow);

        for (var i = 1; i < Notification.MaxAttempts; i++)
        {
            n.MarkAttemptFailed("smtp fora");
            Assert.Equal(NotificationStatus.Pending, n.Status);
        }
        n.MarkAttemptFailed("smtp fora");

        Assert.Equal((NotificationStatus.Failed, Notification.MaxAttempts), (n.Status, n.Attempts));
    }

    [Fact]
    public void ToText_KeepsLinesAndColumns()
    {
        var text = EmailComposer.ToText("<h1>Título</h1>\n<p>Pedido <strong>ABC</strong>: ok</p><table><tr><td>Fone</td><td>2</td></tr></table>");

        Assert.Equal("Título\nPedido ABC: ok\nFone\t2", text);
    }
}
