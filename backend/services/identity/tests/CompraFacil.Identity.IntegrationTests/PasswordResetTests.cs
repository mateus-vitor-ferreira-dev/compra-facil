using System.Net;
using System.Net.Http.Json;
using System.Web;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;

namespace CompraFacil.Identity.IntegrationTests;

/// <summary>Esqueci minha senha: o pedido do link (sempre 202) e a senha nova pelo link.</summary>
public sealed class PasswordResetTests(IdentityFixture fx) : IClassFixture<IdentityFixture>
{
    private const string NewPassword = "senha-nova-456";

    private readonly IdentityClient _api = new(fx.Factory.CreateClient());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> RequestAsync(string email) =>
        _api.Http.PostAsJsonAsync("/v1/accounts/password-reset", new { email }, Ct);

    private Task<HttpResponseMessage> ResetAsync(Guid account, string token, string password = NewPassword) =>
        _api.Http.PostAsJsonAsync("/v1/accounts/password-reset/confirm", new { account_id = account, token, password }, Ct);

    private async Task<Guid> RegisterAsync(string email)
    {
        using var response = await _api.RegisterAsync(email);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await IdentityClient.JsonOf(response)).GetProperty("id").GetGuid();
    }

    private async Task<List<OutboxMessage>> ResetEventsAsync(Guid account) =>
        [.. (await Outbox.OfAsync(fx, account)).Where(m => m.EventType == "PasswordResetRequested")];

    /// <summary>(conta, token) do último link de redefinição, como o front os tira da URL.</summary>
    private async Task<(Guid Account, string Token)> LastLinkAsync(Guid account)
    {
        var url = new Uri(EventEnvelope.TryParse((await ResetEventsAsync(account)).Last().Payload)!.Data.GetProperty("reset_url").GetString()!);
        var query = HttpUtility.ParseQueryString(url.Query);
        return (Guid.Parse(query["conta"]!), query["token"]!);
    }

    private static async Task AssertInvalidLinkAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("common/v1/common.yaml", "/components/schemas/ValidationProblem", problem);
        Assert.Contains("\"field\":\"token\"", problem, StringComparison.Ordinal);
        Assert.Contains("link de redefinição inválido ou vencido", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_ForAnUnknownEmail_Is202_AndPublishesNothing()
    {
        var before = (await Outbox.OfAsync(fx)).Count;

        using var unknown = await RequestAsync($"{Guid.NewGuid():N}@exemplo.com");
        using var blank = await RequestAsync("");

        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, blank.StatusCode);
        Assert.Equal(before, (await Outbox.OfAsync(fx)).Count);
    }

    [Fact]
    public async Task Request_ForAnAccount_PublishesTheLink_ValidForOneHour()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        var account = await RegisterAsync(email);

        using var response = await RequestAsync(email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var message = Assert.Single(await ResetEventsAsync(account)); // Outbox.OfAsync já confere o contrato
        Assert.Equal("identity.account.v1", message.Topic);
        var data = EventEnvelope.TryParse(message.Payload)!.Data;
        Assert.Equal(email, data.GetProperty("email").GetString());
        Assert.Equal("Ana Souza", data.GetProperty("name").GetString());
        Assert.StartsWith("http://localhost:3000/conta/redefinir-senha?", data.GetProperty("reset_url").GetString(), StringComparison.Ordinal);
        Assert.Equal(fx.Clock.GetUtcNow().AddHours(1), data.GetProperty("expires_at").GetDateTimeOffset());
        Assert.Equal(account, (await LastLinkAsync(account)).Account);
    }

    [Fact]
    public async Task Request_Twice_WithinTheInterval_SendsOneEmail_AndAnotherAfter()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        var account = await RegisterAsync(email);

        using (await RequestAsync(email))
        {
        }
        using var again = await RequestAsync(email);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode); // o limite também não aparece na resposta
        Assert.Single(await ResetEventsAsync(account));

        fx.Clock.Advance(TimeSpan.FromMinutes(1));
        using (await RequestAsync(email))
        {
        }

        Assert.Equal(2, (await ResetEventsAsync(account)).Count);
    }

    [Fact]
    public async Task Reset_ChangesThePassword_DropsTheSessions_ConfirmsAndUnlocksTheAccount()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        var account = await RegisterAsync(email);
        using var first = await _api.LoginAsync(email);
        var (_, oldRefresh) = IdentityClient.Tokens(await IdentityClient.JsonOf(first));
        for (var i = 0; i < 5; i++)
        {
            using var wrong = await _api.LoginAsync(email, "senha-errada-1");
        }
        using (var locked = await _api.LoginAsync(email))
        {
            Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
        }
        using (await RequestAsync(email))
        {
        }
        var (id, token) = await LastLinkAsync(account);

        using var reset = await ResetAsync(id, token);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        using var oldPassword = await _api.LoginAsync(email);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        using var refresh = await _api.RefreshAsync(oldRefresh);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode); // a sessão de antes caiu
        using var login = await _api.LoginAsync(email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); // e o bloqueio também
        using var me = await _api.MeAsync(IdentityClient.Tokens(await IdentityClient.JsonOf(login)).Access);
        Assert.True((await IdentityClient.JsonOf(me)).GetProperty("email_confirmed").GetBoolean());
    }

    [Fact]
    public async Task Reset_TheSameLinkTwice_TheSecondIsInvalid()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        var account = await RegisterAsync(email);
        using (await RequestAsync(email))
        {
        }
        var (_, token) = await LastLinkAsync(account);

        using var first = await ResetAsync(account, token);
        using var second = await ResetAsync(account, token, "outra-senha-789");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        await AssertInvalidLinkAsync(second);
        using var login = await _api.LoginAsync(email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); // vale a senha da primeira troca
    }

    [Fact]
    public async Task Reset_AfterOneHour_IsInvalid()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        var account = await RegisterAsync(email);
        using (await RequestAsync(email))
        {
        }
        var (_, token) = await LastLinkAsync(account);

        fx.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        using var expired = await ResetAsync(account, token);

        await AssertInvalidLinkAsync(expired);
        using var login = await _api.LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); // a senha continua a mesma
    }

    [Fact]
    public async Task Reset_WrongTokenOrUnknownAccount_SameGeneric422()
    {
        var account = await RegisterAsync($"{Guid.NewGuid():N}@exemplo.com");
        var otherEmail = $"{Guid.NewGuid():N}@exemplo.com";
        var other = await RegisterAsync(otherEmail);
        using (await RequestAsync(otherEmail))
        {
        }
        var (_, otherToken) = await LastLinkAsync(other);
        var (_, confirmationToken) = await Outbox.LastLinkAsync(fx, account);

        using var wrong = await ResetAsync(account, otherToken); // token de outra conta
        using var garbage = await ResetAsync(account, "nao-e-um-token");
        using var unknown = await ResetAsync(Guid.NewGuid(), otherToken);
        using var confirmation = await ResetAsync(account, confirmationToken); // o link de confirmação não troca senha

        foreach (var r in new[] { wrong, garbage, unknown, confirmation })
        {
            await AssertInvalidLinkAsync(r);
        }
    }

    [Fact]
    public async Task Reset_WithAWeakPassword_Is422OnPassword_AndKeepsTheLink()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        var account = await RegisterAsync(email);
        using (await RequestAsync(email))
        {
        }
        var (_, token) = await LastLinkAsync(account);

        using var weak = await ResetAsync(account, token, "aaaabbbb");
        using var good = await ResetAsync(account, token);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, weak.StatusCode);
        Assert.Contains("\"field\":\"password\"", await weak.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, good.StatusCode);
    }
}
