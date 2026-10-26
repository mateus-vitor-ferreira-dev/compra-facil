using CompraFacil.Identity.Domain;

namespace CompraFacil.Identity.UnitTests;

public sealed class SigningKeyScheduleTests
{
    private static readonly DateTimeOffset s_t0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly KeyRotationPolicy s_policy = KeyRotationPolicy.Default; // 30 dias, 1 dia antes, token de 15 min

    private static SigningKeyLifetime Key(string id, DateTimeOffset activatesAt) => SigningKeySchedule.Plan(id, activatesAt, s_policy);

    [Fact]
    public void NoKeys_CreatesOneActiveNow()
    {
        Assert.Equal(s_t0, SigningKeySchedule.NextActivation([], s_t0, s_policy));
        Assert.Null(SigningKeySchedule.Active([], s_t0));
    }

    [Fact]
    public void FreshKey_NeedsNothing_UntilThePublishAheadWindow()
    {
        var a = Key("a", s_t0);

        Assert.Null(SigningKeySchedule.NextActivation([a], s_t0.AddDays(28), s_policy));
        Assert.Null(SigningKeySchedule.NextActivation([a], s_t0.AddDays(29).AddSeconds(-1), s_policy));
        // a um dia da troca: a sucessora é criada já com a data de começar a assinar
        Assert.Equal(s_t0.AddDays(30), SigningKeySchedule.NextActivation([a], s_t0.AddDays(29), s_policy));
    }

    [Fact]
    public void Successor_IsPublishedBeforeSigning_ThenTakesOver_AndTheOldOneLeavesTheJwksLater()
    {
        var a = Key("a", s_t0);
        var b = Key("b", s_t0.AddDays(30));
        SigningKeyLifetime[] keys = [a, b];

        // dia 29: b já publicada (b.PublishedAt), mas quem assina ainda é a
        Assert.Equal("a", SigningKeySchedule.Active(keys, s_t0.AddDays(29.5))!.KeyId);
        Assert.True(b.PublishedAt(s_t0.AddDays(29.5)));
        Assert.Null(SigningKeySchedule.NextActivation(keys, s_t0.AddDays(29.5), s_policy));

        // dia 30: b assume; a continua no JWKS para validar os tokens que assinou
        Assert.Equal("b", SigningKeySchedule.Active(keys, s_t0.AddDays(30))!.KeyId);
        Assert.True(a.PublishedAt(s_t0.AddDays(30).AddMinutes(14)));

        // a sai do JWKS depois da folga de um dia e do último token dela vencer
        Assert.False(a.PublishedAt(s_t0.AddDays(31).AddMinutes(15)));
    }

    [Fact]
    public void ServiceDownThroughTheWindow_SuccessorAtLeastPublishAheadAway()
    {
        var a = Key("a", s_t0);

        // subiu meio dia depois da data de troca: a ainda assina (folga), b vem um dia depois de agora
        var now = s_t0.AddDays(30.5);
        Assert.Equal("a", SigningKeySchedule.Active([a], now)!.KeyId);
        Assert.Equal(now.AddDays(1), SigningKeySchedule.NextActivation([a], now, s_policy));
    }

    [Fact]
    public void ExpiredWithoutSuccessor_CreatesOneActiveNow()
    {
        var a = Key("a", s_t0);
        var now = s_t0.AddDays(40);

        Assert.Null(SigningKeySchedule.Active([a], now));
        Assert.Equal(now, SigningKeySchedule.NextActivation([a], now, s_policy));
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(30, 31)]
    [InlineData(0, 1)]
    public void Policy_PublishAheadShorterThanRotation(int rotateDays, int aheadDays)
    {
        var policy = new KeyRotationPolicy(TimeSpan.FromDays(rotateDays), TimeSpan.FromDays(aheadDays), TimeSpan.FromMinutes(15));
        Assert.Throws<InvalidOperationException>(policy.Validate);
    }
}
