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
    [InlineData("supplier_failed", null, "fornecedor")]
    [InlineData("payment_failed", "insufficient_funds", "limite insuficiente")]
    [InlineData("payment_failed", "card_declined", "cartão foi recusado")]
    [InlineData("payment_failed", "pix_expired", "PIX não foi pago")]
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

    private static readonly DateTimeOffset s_now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static EmailConfirmationRequestedData Confirmation(string url, string name = "Ana <b>Souza</b>", double hours = 24) =>
        new(Guid.NewGuid(), "ana@exemplo.com", name, new Uri(url), s_now.AddHours(hours));

    [Fact]
    public async Task ConfirmEmail_HasTheLink_TheFirstName_AndTheValidity()
    {
        var url = "http://localhost:3000/conta/confirmar-email?conta=0199&token=abc_-123";

        var email = await EmailComposer.ConfirmEmailAsync(Confirmation(url), s_now);

        Assert.Equal("Confirme o seu e-mail", email.Subject);
        Assert.Contains("href=\"http://localhost:3000/conta/confirmar-email?conta=0199&amp;token=abc_-123\"", email.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Ana", email.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", email.HtmlBody, StringComparison.Ordinal); // nome escapado (e só o primeiro)
        Assert.Contains("24 horas", email.TextBody, StringComparison.Ordinal);
        Assert.Contains(url, email.TextBody, StringComparison.Ordinal); // o texto puro também leva o link
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,oi")]
    public async Task ConfirmEmail_OnlyHttpLinks(string url) =>
        await Assert.ThrowsAsync<CompraFacil.BuildingBlocks.Messaging.PoisonMessageException>(() =>
            EmailComposer.ConfirmEmailAsync(Confirmation(url), s_now));

    [Theory]
    [InlineData(0.9, "1 hora")]
    [InlineData(24, "24 horas")]
    [InlineData(72, "3 dias")]
    public void ValidFor_ReadsLikeAPerson(double hours, string expected) =>
        Assert.Equal(expected, EmailComposer.ValidFor(TimeSpan.FromHours(hours)));

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
