using System.Net;
using CompraFacil.BuildingBlocks.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace CompraFacil.Identity.IntegrationTests;

/// <summary>Login, bloqueio, refresh com rotação e detecção de reuso, logout.</summary>
public sealed class SessionsTests(IdentityFixture fx) : IClassFixture<IdentityFixture>
{
    private readonly IdentityClient _api = new(fx.Factory.CreateClient());

    private static string NewEmail() => $"{Guid.NewGuid():N}@exemplo.com";

    [Fact]
    public async Task Login_ReturnsTheTokens_NotCached_WithTheContractClaims()
    {
        var email = NewEmail();
        using (await _api.RegisterAsync(email))
        {
        }

        using var response = await _api.LoginAsync(email.ToUpperInvariant()); // e-mail sem diferenciar caixa

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await IdentityClient.JsonOf(response);
        ContractAssert.AssertSchema("identity.v1.yaml", "/components/schemas/Tokens", body.GetRawText());
        Assert.Equal(900, body.GetProperty("expires_in").GetInt32());

        var jwt = new JsonWebToken(body.GetProperty("access_token").GetString());
        Assert.Equal("RS256", jwt.Alg);
        Assert.False(string.IsNullOrEmpty(jwt.Kid));
        Assert.Equal("compra-facil-identity", jwt.Issuer);
        Assert.Equal(["compra-facil"], jwt.Audiences);
        Assert.Equal(email, jwt.GetClaim("email").Value);
        Assert.Equal("customer", jwt.GetClaim("roles").Value);
        Assert.True(Guid.TryParse(jwt.Subject, out _));
        Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.IssuedAt);
    }

    [Fact]
    public async Task Login_UnknownEmailOrWrongPassword_SameGeneric401()
    {
        var email = NewEmail();
        using (await _api.RegisterAsync(email))
        {
        }

        using var unknown = await _api.LoginAsync(NewEmail());
        using var wrong = await _api.LoginAsync(email, "senha-errada-1");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var a = await IdentityClient.JsonOf(unknown);
        var b = await IdentityClient.JsonOf(wrong);
        Assert.Equal(a.GetProperty("detail").GetString(), b.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Login_FiveWrongPasswords_LocksTheAccount_EvenForTheRightOne()
    {
        var email = NewEmail();
        using (await _api.RegisterAsync(email))
        {
        }
        for (var i = 0; i < 4; i++)
        {
            using var wrong = await _api.LoginAsync(email, "senha-errada-1");
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        using var fifth = await _api.LoginAsync(email, "senha-errada-1");
        using var right = await _api.LoginAsync(email);

        Assert.Equal(HttpStatusCode.Locked, fifth.StatusCode);
        Assert.Equal(HttpStatusCode.Locked, right.StatusCode);
        var retryAfter = right.Headers.RetryAfter?.Delta;
        Assert.NotNull(retryAfter);
        // o fim do bloqueio vem do relógio do sistema (Identity), não do relógio falso do teste: folga de 1 min
        Assert.InRange(retryAfter.Value, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16));
        ContractAssert.AssertSchema("common/v1/common.yaml", "/components/schemas/Problem", (await IdentityClient.JsonOf(right)).GetRawText());
    }

    [Fact]
    public async Task Refresh_RotatesTheRefreshToken_AndTheNewAccessTokenWorks()
    {
        var (_, refresh) = await _api.NewSessionAsync();

        using var response = await _api.RefreshAsync(refresh);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await IdentityClient.JsonOf(response);
        ContractAssert.AssertSchema("identity.v1.yaml", "/components/schemas/Tokens", body.GetRawText());
        var (access, next) = IdentityClient.Tokens(body);
        Assert.NotEqual(refresh, next);
        using var me = await _api.MeAsync(access);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReusingARotatedToken_EndsTheWholeSession()
    {
        var (_, first) = await _api.NewSessionAsync();
        using var rotated = await _api.RefreshAsync(first);
        var (_, second) = IdentityClient.Tokens(await IdentityClient.JsonOf(rotated));

        using var reuse = await _api.RefreshAsync(first); // quem copiou o primeiro token
        using var legit = await _api.RefreshAsync(second); // e o dono, que tinha o novo

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, legit.StatusCode);
    }

    [Fact]
    public async Task Refresh_TwoAtTheSameTime_CountsAsReuse_AndEndsTheSession()
    {
        var (_, refresh) = await _api.NewSessionAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _api.RefreshAsync(refresh)));
        try
        {
            // um passa (chegou primeiro), os outros encontram o token já trocado e derrubam a sessão
            var winner = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.All(responses.Where(r => r != winner), r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
            var (_, next) = IdentityClient.Tokens(await IdentityClient.JsonOf(winner));
            using var afterwards = await _api.RefreshAsync(next);
            Assert.Equal(HttpStatusCode.Unauthorized, afterwards.StatusCode);
        }
        finally
        {
            foreach (var r in responses)
            {
                r.Dispose();
            }
        }
    }

    [Fact]
    public async Task Lockout_BlocksLogin_ButKeepsOpenSessions()
    {
        var email = NewEmail();
        var (_, refresh) = await _api.NewSessionAsync(email);
        for (var i = 0; i < 5; i++)
        {
            using var wrong = await _api.LoginAsync(email, "senha-errada-1");
        }
        using (var login = await _api.LoginAsync(email))
        {
            Assert.Equal(HttpStatusCode.Locked, login.StatusCode);
        }

        using var response = await _api.RefreshAsync(refresh);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao-e-um-token")]
    public async Task Refresh_UnknownToken_Is401(string token)
    {
        using var response = await _api.RefreshAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_EndsTheSession_AndIsIdempotent()
    {
        var (_, refresh) = await _api.NewSessionAsync();
        using var rotated = await _api.RefreshAsync(refresh);
        var (_, current) = IdentityClient.Tokens(await IdentityClient.JsonOf(rotated));

        using var revoke = await _api.RevokeAsync(current);
        using var again = await _api.RevokeAsync(current);
        using var unknown = await _api.RevokeAsync("nao-e-um-token");
        using var refreshAfter = await _api.RefreshAsync(current);

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfter.StatusCode);
    }
}
