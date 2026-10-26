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

/// <summary>O serviço ficou fora durante toda a janela de troca: a chave atual é esticada, sem intervalo sem chave.</summary>
public sealed class LateRotationTests(IdentityFixture fx) : IClassFixture<IdentityFixture>
{
    private readonly IdentityClient _api = new(fx.Factory.CreateClient());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MissedWindow_CurrentKeyKeepsSigning_UntilTheAnnouncedSuccessorStarts()
    {
        var first = new JsonWebToken((await _api.NewSessionAsync()).Access).Kid;

        // primeira requisição só no dia 30,5: passou da janela de troca (dias 29 a 30)
        fx.Clock.Advance(TimeSpan.FromDays(30.5));
        Assert.Equal(first, new JsonWebToken((await _api.NewSessionAsync()).Access).Kid);
        using var jwks = await _api.Http.GetAsync(new Uri("/.well-known/jwks.json", UriKind.Relative), Ct);
        var kids = new JsonWebKeySet(await jwks.Content.ReadAsStringAsync(Ct)).Keys.Select(k => k.Kid).ToList();
        var next = Assert.Single(kids, k => k != first);

        // dia 31,25: sem a extensão, a primeira já teria parado (dia 31) e uma chave não anunciada assinaria
        fx.Clock.Advance(TimeSpan.FromDays(0.75));
        Assert.Equal(first, new JsonWebToken((await _api.NewSessionAsync()).Access).Kid);

        // dia 31,5: a sucessora anunciada um dia antes assume
        fx.Clock.Advance(TimeSpan.FromDays(0.25));
        Assert.Equal(next, new JsonWebToken((await _api.NewSessionAsync()).Access).Kid);
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
