using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using CompraFacil.BuildingBlocks.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CompraFacil.Identity.IntegrationTests;

/// <summary>
/// O que os outros serviços fazem com o token: validam sozinhos pelo JWKS. E o que não pode passar:
/// assinatura de outra chave, HS256 com a chave pública, token vencido.
/// </summary>
public sealed class TokensTests(IdentityFixture fx) : IClassFixture<IdentityFixture>
{
    private readonly IdentityClient _api = new(fx.Factory.CreateClient());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<JsonWebKeySet> JwksAsync()
    {
        using var response = await _api.Http.GetAsync(new Uri("/.well-known/jwks.json", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(5), response.Headers.CacheControl?.MaxAge);
        var json = await response.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("identity.v1.yaml", "/components/schemas/Jwks", json);
        return new JsonWebKeySet(json);
    }

    // a validação de um serviço consumidor: só o JWKS, o emissor e a audiência
    private async Task<TokenValidationResult> ValidateLikeAConsumerAsync(string token, JsonWebKeySet jwks) =>
        await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "compra-facil-identity",
            ValidAudience = "compra-facil",
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKeys = jwks.GetSigningKeys(),
            LifetimeValidator = (_, expires, _, _) => expires > fx.Clock.GetUtcNow().UtcDateTime,
        });

    [Fact]
    public async Task AccessToken_IsValidatedWithOnlyTheJwks()
    {
        var (access, _) = await _api.NewSessionAsync();

        var result = await ValidateLikeAConsumerAsync(access, await JwksAsync());

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Contains(result.ClaimsIdentity.Claims, c => c.Type == "roles" && c.Value == "customer");
    }

    [Fact]
    public async Task OpenIdConfiguration_PointsToTheJwks()
    {
        using var response = await _api.Http.GetAsync(new Uri("/.well-known/openid-configuration", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("identity.v1.yaml", "/components/schemas/OpenIdConfiguration", json);
        var body = await IdentityClient.JsonOf(response);
        Assert.Equal("compra-facil-identity", body.GetProperty("issuer").GetString());
        Assert.EndsWith("/.well-known/jwks.json", body.GetProperty("jwks_uri").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenIdConfiguration_BehindTheGateway_KeepsTheStrippedPrefix()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/openid-configuration");
        request.Headers.Add("X-Forwarded-Prefix", "/identity");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await _api.Http.SendAsync(request, Ct);

        var jwksUri = (await IdentityClient.JsonOf(response)).GetProperty("jwks_uri").GetString();
        Assert.Equal("https://localhost/identity/.well-known/jwks.json", jwksUri);
    }

    [Fact]
    public async Task Me_RejectsATokenSignedByAnotherKey_WithTheSameKid()
    {
        var (access, _) = await _api.NewSessionAsync();
        var real = new JsonWebToken(access);
        using var attacker = RSA.Create(2048);

        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = real.Issuer,
            Audience = "compra-facil",
            Claims = new Dictionary<string, object> { ["sub"] = real.Subject, ["roles"] = "admin" },
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(attacker) { KeyId = real.Kid }, SecurityAlgorithms.RsaSha256),
        });
        using var response = await _api.MeAsync(forged);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_RejectsHs256SignedWithThePublicKey()
    {
        var (access, _) = await _api.NewSessionAsync();
        var real = new JsonWebToken(access);
        var jwk = (await JwksAsync()).Keys.Single(k => k.Kid == real.Kid);

        // confusão de algoritmo: usar o módulo público (que todos conhecem) como segredo HMAC
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = real.Issuer,
            Audience = "compra-facil",
            Claims = new Dictionary<string, object> { ["sub"] = real.Subject },
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwk.N)) { KeyId = real.Kid }, SecurityAlgorithms.HmacSha256),
        });
        using var response = await _api.MeAsync(forged);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Jwks_NeverExposesThePrivateKey()
    {
        using var response = await _api.Http.GetAsync(new Uri("/.well-known/jwks.json", UriKind.Relative), Ct);
        var keys = (await IdentityClient.JsonOf(response)).GetProperty("keys").EnumerateArray().ToList();

        Assert.NotEmpty(keys);
        Assert.All(keys, k => Assert.Equal(
            ["alg", "e", "kid", "kty", "n", "use"],
            k.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task Register_ToMe_EndToEnd_UsesOnlyTheContractFields()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        using var registered = await _api.Http.PostAsJsonAsync("/v1/accounts", new { email, password = "senha-boa-123", name = "Bia" }, Ct);
        using var login = await _api.Http.PostAsJsonAsync("/v1/sessions", new { email, password = "senha-boa-123" }, Ct);
        var (access, _) = IdentityClient.Tokens(await IdentityClient.JsonOf(login));

        using var me = await _api.MeAsync(access);

        Assert.Equal(
            (await IdentityClient.JsonOf(registered)).GetRawText(),
            (await IdentityClient.JsonOf(me)).GetRawText());
    }
}
