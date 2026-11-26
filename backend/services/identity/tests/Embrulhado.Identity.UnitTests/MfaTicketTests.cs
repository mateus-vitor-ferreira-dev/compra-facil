using Embrulhado.Identity.Domain;

namespace Embrulhado.Identity.UnitTests;

public sealed class MfaTicketTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LoginTicket_LastsFiveMinutes_OnlyForLogin_WithTheSameStamp()
    {
        var ticket = MfaTicket.Issue(Guid.NewGuid(), MfaTicketPurpose.Login, "carimbo", [1], s_now);

        Assert.True(ticket.IsUsable(MfaTicketPurpose.Login, "carimbo", s_now.AddMinutes(4)));
        Assert.False(ticket.IsUsable(MfaTicketPurpose.Login, "carimbo", s_now.AddMinutes(5)));
        Assert.False(ticket.IsUsable(MfaTicketPurpose.Setup, "carimbo", s_now)); // o do login não cadastra autenticador
        Assert.False(ticket.IsUsable(MfaTicketPurpose.Login, "outro", s_now)); // a senha mudou no meio
    }

    [Fact]
    public void SetupTicket_LastsFifteenMinutes()
    {
        var ticket = MfaTicket.Issue(Guid.NewGuid(), MfaTicketPurpose.Setup, null, [1], s_now);

        Assert.Equal(s_now.AddMinutes(15), ticket.ExpiresAt);
        Assert.True(ticket.IsUsable(MfaTicketPurpose.Setup, null, s_now.AddMinutes(14)));
        Assert.False(ticket.IsUsable(MfaTicketPurpose.Login, null, s_now));
    }

    [Fact]
    public void RefreshToken_CarriesTheSecondFactor_ToTheRotatedAndTheSibling()
    {
        var lifetime = TimeSpan.FromDays(30);
        var mfa = RefreshToken.Issue(Guid.NewGuid(), "carimbo", [1], s_now, lifetime, mfa: true);
        var plain = RefreshToken.Issue(Guid.NewGuid(), "carimbo", [2], s_now, lifetime);

        var next = mfa.Rotate([3], s_now.AddMinutes(1), lifetime);
        var sibling = mfa.Sibling([4], s_now.AddMinutes(1), lifetime, TimeSpan.FromSeconds(10));

        Assert.True(next.Mfa);
        Assert.True(sibling.Mfa);
        Assert.False(plain.Rotate([5], s_now.AddMinutes(1), lifetime).Mfa);
    }
}
