using CompraFacil.Identity.Domain;

namespace CompraFacil.Identity.UnitTests;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan s_lifetime = TimeSpan.FromDays(30);

    [Fact]
    public void Rotate_RevokesThisOne_AndTheNextIsSameFamily()
    {
        var first = RefreshToken.Issue(Guid.NewGuid(), [1], s_now, s_lifetime);

        var next = first.Rotate([2], s_now.AddDays(1), s_lifetime);

        Assert.False(first.IsActive(s_now.AddDays(1)));
        Assert.True(first.WasRotated);
        Assert.Equal(next.Id, first.ReplacedById);
        Assert.Equal(first.FamilyId, next.FamilyId);
        Assert.Equal(first.UserId, next.UserId);
        Assert.True(next.IsActive(s_now.AddDays(1)));
        Assert.Equal(s_now.AddDays(31), next.ExpiresAt); // a validade conta do refresh, não do login
    }

    [Fact]
    public void RotatedWithin_OnlyRightAfterTheRotation()
    {
        var interval = TimeSpan.FromSeconds(10);
        var first = RefreshToken.Issue(Guid.NewGuid(), [1], s_now, s_lifetime);
        Assert.False(first.RotatedWithin(s_now, interval)); // ainda não foi trocado

        first.Rotate([2], s_now, s_lifetime);

        Assert.True(first.RotatedWithin(s_now.AddSeconds(10), interval));
        Assert.False(first.RotatedWithin(s_now.AddSeconds(11), interval));
        Assert.False(first.RotatedWithin(s_now, TimeSpan.Zero)); // intervalo desligado
    }

    [Fact]
    public void RotatedWithin_IsFalseForLogoutOrReuse()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), [1], s_now, s_lifetime);
        token.Revoke(s_now, RevocationReason.Logout);

        Assert.False(token.RotatedWithin(s_now, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Sibling_IsSameFamily_AndOnlyInsideTheInterval()
    {
        var interval = TimeSpan.FromSeconds(10);
        var first = RefreshToken.Issue(Guid.NewGuid(), [1], s_now, s_lifetime);
        first.Rotate([2], s_now, s_lifetime);

        var sibling = first.Sibling([3], s_now.AddSeconds(2), s_lifetime, interval);

        Assert.Equal((first.FamilyId, first.UserId), (sibling.FamilyId, sibling.UserId));
        Assert.True(sibling.IsActive(s_now.AddSeconds(2)));
        Assert.True(first.WasRotated); // o original continua trocado
        Assert.Throws<InvalidOperationException>(() => first.Sibling([4], s_now.AddSeconds(11), s_lifetime, interval));
    }

    [Fact]
    public void Expired_IsInactive_AndCannotRotate()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), [1], s_now, s_lifetime);

        Assert.False(token.IsActive(s_now + s_lifetime));
        Assert.Throws<InvalidOperationException>(() => token.Rotate([2], s_now + s_lifetime, s_lifetime));
    }

    [Fact]
    public void Revoke_KeepsTheFirstReason()
    {
        var token = RefreshToken.Issue(Guid.NewGuid(), [1], s_now, s_lifetime);

        token.Revoke(s_now, RevocationReason.Logout);
        token.Revoke(s_now.AddMinutes(1), RevocationReason.Reuse);

        Assert.Equal(RevocationReason.Logout, token.RevocationReason);
        Assert.Equal(s_now, token.RevokedAt);
        Assert.False(token.WasRotated);
    }
}
