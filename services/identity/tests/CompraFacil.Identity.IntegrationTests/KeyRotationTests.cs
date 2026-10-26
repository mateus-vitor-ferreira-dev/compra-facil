using System.Net;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CompraFacil.Identity.IntegrationTests;

/// <summary>Rotação da chave de assinatura pelo relógio, com o banco de verdade, ao longo de um mês.</summary>
public sealed class KeyRotationTests(IdentityFixture fx) : IClassFixture<IdentityFixture>
{
    private readonly IdentityClient _api = new(fx.Factory.CreateClient());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<IReadOnlyList<string>> KidsAsync()
    {
        using var response = await _api.Http.GetAsync(new Uri("/.well-known/jwks.json", UriKind.Relative), Ct);
        return [.. new JsonWebKeySet(await response.Content.ReadAsStringAsync(Ct)).Keys.Select(k => k.Kid)];
    }

    private static string KidOf(string accessToken) => new JsonWebToken(accessToken).Kid;

    [Fact]
    public async Task Rotation_PublishesAhead_SwitchesOnTime_AndRetiresTheOldKey()
    {
        var first = KidOf((await _api.NewSessionAsync()).Access);
        Assert.Equal([first], await KidsAsync());

        // dia 29: a próxima chave aparece no JWKS, mas ainda não assina
        fx.Clock.Advance(TimeSpan.FromDays(29) + TimeSpan.FromMinutes(1));
        var next = Assert.Single(await KidsAsync(), k => k != first);
        Assert.Equal(first, KidOf((await _api.NewSessionAsync()).Access));

        // 5 min antes da troca: um token da chave antiga...
        fx.Clock.Advance(TimeSpan.FromDays(1) - TimeSpan.FromMinutes(6));
        var (lastOfFirst, _) = await _api.NewSessionAsync();
        Assert.Equal(first, KidOf(lastOfFirst));

        // ...continua valendo depois dela, quando a nova já assina
        fx.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(next, KidOf((await _api.NewSessionAsync()).Access));
        using (var me = await _api.MeAsync(lastOfFirst))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }

        // um dia de folga + o último token dela vencido: a antiga sai do JWKS
        fx.Clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(16));
        Assert.Equal([next], await KidsAsync());
    }
}

/// <summary>Validade dos tokens: access de 15 min, refresh de 30 dias.</summary>
public sealed class ExpirationTests(IdentityFixture fx) : IClassFixture<IdentityFixture>
{
    private readonly IdentityClient _api = new(fx.Factory.CreateClient());

    [Fact]
    public async Task AccessToken_ExpiresAfterFifteenMinutes()
    {
        var (access, _) = await _api.NewSessionAsync();
        using (var ok = await _api.MeAsync(access))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        fx.Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(31)); // passa da tolerância de 30 s
        using var expired = await _api.MeAsync(access);

        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task Session_ExpiresAfterThirtyDaysWithoutRefresh()
    {
        var (_, refresh) = await _api.NewSessionAsync();

        fx.Clock.Advance(TimeSpan.FromDays(30));
        using var response = await _api.RefreshAsync(refresh);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
