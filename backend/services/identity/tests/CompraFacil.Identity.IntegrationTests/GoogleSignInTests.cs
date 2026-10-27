using System.Net;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Testing;

namespace CompraFacil.Identity.IntegrationTests;

/// <summary>POST /v1/sessions/google e /google/link: a conta pelo sub do Google, o vínculo só com a senha.</summary>
public sealed class GoogleSignInTests(IdentityFixture fx) : IClassFixture<IdentityFixture>
{
    private readonly IdentityClient _api = new(fx.Factory.CreateClient());

    private static string NewEmail() => $"google-{Guid.NewGuid():N}@exemplo.com";

    private static string NewSub() => Guid.NewGuid().ToString("N");

    private async Task<JsonElement> MeOf(HttpResponseMessage tokens) => await MeOf(await IdentityClient.JsonOf(tokens));

    private async Task<JsonElement> MeOf(JsonElement tokens)
    {
        var (access, _) = IdentityClient.Tokens(tokens);
        using var me = await _api.MeAsync(access);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        return await IdentityClient.JsonOf(me);
    }

    [Fact]
    public async Task NewGoogleAccount_IsCreatedConfirmed_AndTheSameSubComesBackToIt()
    {
        var (sub, email) = (NewSub(), NewEmail());

        using var first = await _api.GoogleAsync(fx.Google.IdToken(sub, email, "n-1"), "n-1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.CacheControl?.NoStore);
        var tokens = await IdentityClient.JsonOf(first);
        ContractAssert.AssertSchema("identity.v1.yaml", "/components/schemas/Tokens", tokens.GetRawText());
        var account = await MeOf(tokens);
        Assert.Equal(email, account.GetProperty("email").GetString());
        Assert.Equal("Ana do Google", account.GetProperty("name").GetString());
        Assert.True(account.GetProperty("email_confirmed").GetBoolean());
        Assert.Equal(["customer"], account.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        // a pessoa trocou o e-mail no Google: o sub é o mesmo, a conta também
        using var again = await _api.GoogleAsync(fx.Google.IdToken(sub, NewEmail(), "n-2"), "n-2");
        Assert.Equal(account.GetProperty("id").GetString(), (await MeOf(again)).GetProperty("id").GetString());
    }

    [Fact]
    public async Task EmailWithPassword_NeedsThePasswordToLink_ThenGoogleAlone()
    {
        var (sub, email) = (NewSub(), NewEmail());
        using (var registered = await _api.RegisterAsync(email))
        {
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        }
        var token = fx.Google.IdToken(sub, email, "n-1");

        using var conflict = await _api.GoogleAsync(token, "n-1");
        using var wrong = await _api.LinkGoogleAsync(token, "n-1", "senha-errada-1");
        using var linked = await _api.LinkGoogleAsync(token, "n-1", "senha-boa-123");
        using var later = await _api.GoogleAsync(fx.Google.IdToken(sub, email, "n-2"), "n-2");

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("Conta já existe", (await IdentityClient.JsonOf(conflict)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        Assert.Equal((await MeOf(linked)).GetProperty("id").GetString(), (await MeOf(later)).GetProperty("id").GetString());
    }

    public static TheoryData<string> BadTokens => ["nonce", "aud", "chave", "nao-verificado", "vencido", "lixo"];

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task InvalidIdToken_Is401_AndCreatesNothing(string broken)
    {
        var (sub, email) = (NewSub(), NewEmail());
        var token = broken switch
        {
            "nonce" => fx.Google.IdToken(sub, email, "outro-login"),
            "aud" => fx.Google.IdToken(sub, email, "n", audience: "outro-app"),
            "chave" => fx.Google.IdToken(sub, email, "n", otherKey: true),
            "nao-verificado" => fx.Google.IdToken(sub, email, "n", verified: false),
            "vencido" => fx.Google.IdToken(sub, email, "n", expiresIn: TimeSpan.FromMinutes(-10)),
            _ => "nao-e-um-jwt",
        };

        using var response = await _api.GoogleAsync(token, "n");
        using var thenValid = await _api.LinkGoogleAsync(fx.Google.IdToken(sub, email, "n"), "n", "qualquer-senha");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, thenValid.StatusCode); // a conta não foi criada
    }
}
