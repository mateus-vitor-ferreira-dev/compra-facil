using System.Net;
using System.Net.Http.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using Microsoft.AspNetCore.WebUtilities;

namespace CompraFacil.Identity.IntegrationTests;

/// <summary>Compra sem conta: a sessão do convidado, o link do e-mail e o reenvio.</summary>
public sealed class GuestCheckoutTests(IdentityFixture fx) : IClassFixture<IdentityFixture>
{
    private readonly IdentityClient _api = new(fx.Factory.CreateClient());
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> StartAsync(string email, Guid? key = null, string name = "Bia Lima") =>
        _api.Http.PostAsJsonAsync("/v1/sessions/guest", new { email, name, checkout_key = key ?? Guid.NewGuid() }, Ct);

    private Task<HttpResponseMessage> AccessAsync(string token) =>
        _api.Http.PostAsJsonAsync("/v1/sessions/guest/access", new { token }, Ct);

    private Task<HttpResponseMessage> ResendAsync(string email) =>
        _api.Http.PostAsJsonAsync("/v1/sessions/guest/access/resend", new { email }, Ct);

    private static async Task<string> AccessTokenOf(HttpResponseMessage response) =>
        (await IdentityClient.JsonOf(response)).GetProperty("access_token").GetString()!;

    private async Task<(string Id, string Email, bool IsGuest)> MeAsync(string accessToken)
    {
        using var me = await _api.MeAsync(accessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = await IdentityClient.JsonOf(me);
        return (body.GetProperty("id").GetString()!, body.GetProperty("email").GetString()!, body.GetProperty("is_guest").GetBoolean());
    }

    private async Task<string> LinkTokenAsync(Guid accountId)
    {
        var message = (await Outbox.OfAsync(fx, accountId)).Last(m => m.EventType == "GuestAccessRequested");
        var url = new Uri(EventEnvelope.TryParse(message.Payload)!.Data.GetProperty("access_url").GetString()!);
        return QueryHelpers.ParseQuery(url.Query)["token"].ToString();
    }

    [Fact]
    public async Task Start_OpensSession_WithTheTypedEmail()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";

        using var response = await StartAsync(email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await IdentityClient.JsonOf(response);
        ContractAssert.AssertSchema("identity.v1.yaml", "/components/schemas/Tokens", tokens.GetRawText());
        var (id, seen, isGuest) = await MeAsync(tokens.GetProperty("access_token").GetString()!);
        Assert.Equal(email, seen);
        Assert.True(isGuest);
        Assert.Contains(await Outbox.OfAsync(fx, Guid.Parse(id)), m => m.EventType == "GuestAccessRequested");
    }

    [Fact]
    public async Task Start_SameForm_IsTheSamePurchase_WithTheSameLinkEmail()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        var key = Guid.NewGuid();

        using var first = await StartAsync(email, key);
        using var again = await StartAsync(email.ToUpperInvariant(), key);

        var (one, _, _) = await MeAsync(await AccessTokenOf(first));
        var (two, _, _) = await MeAsync(await AccessTokenOf(again));
        Assert.Equal(one, two);
        Assert.Single(await Outbox.OfAsync(fx, Guid.Parse(one)), m => m.EventType == "GuestAccessRequested");
    }

    [Fact]
    public async Task Start_SameFormWithOtherEmail_Is409()
    {
        var key = Guid.NewGuid();
        using var first = await StartAsync($"{Guid.NewGuid():N}@exemplo.com", key);

        using var other = await StartAsync($"{Guid.NewGuid():N}@exemplo.com", key);

        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
    }

    [Fact]
    public async Task Start_TypingSomeoneElsesEmail_DoesNotReachTheirPurchaseNorAccount()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        using var registered = await _api.RegisterAsync(email);
        var account = (await IdentityClient.JsonOf(registered)).GetProperty("id").GetString()!;
        using var victim = await StartAsync(email);
        var (victimGuest, _, _) = await MeAsync(await AccessTokenOf(victim));

        using var attacker = await StartAsync(email);

        var (attackerGuest, _, _) = await MeAsync(await AccessTokenOf(attacker));
        Assert.NotEqual(victimGuest, attackerGuest);
        Assert.NotEqual(account, attackerGuest);
    }

    [Fact]
    public async Task Start_DoesNotBlockNorEnableThePasswordLogin()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        using var guest = await StartAsync(email);

        using var withoutAccount = await _api.LoginAsync(email);
        Assert.Equal(HttpStatusCode.Unauthorized, withoutAccount.StatusCode);

        using var registered = await _api.RegisterAsync(email);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
    }

    [Fact]
    public async Task Access_WithTheLink_OpensThatPurchase_MoreThanOnce()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        using var started = await StartAsync(email);
        var (id, _, _) = await MeAsync(await AccessTokenOf(started));
        var token = await LinkTokenAsync(Guid.Parse(id));

        using var first = await AccessAsync(token);
        using var again = await AccessAsync(token);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(id, (await MeAsync(await AccessTokenOf(again))).Id);
    }

    [Fact]
    public async Task Access_UnknownOrExpired_IsTheSame401()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        using var started = await StartAsync(email);
        var (id, _, _) = await MeAsync(await AccessTokenOf(started));
        var token = await LinkTokenAsync(Guid.Parse(id));

        using var unknown = await AccessAsync("nao-existe");
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);

        fx.Clock.Advance(TimeSpan.FromDays(181));
        using var expired = await AccessAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task Resend_SendsANewLink_AtMostOncePerInterval_AndNeverSaysIfThereIsAPurchase()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        using var started = await StartAsync(email);
        var account = Guid.Parse((await MeAsync(await AccessTokenOf(started))).Id);

        using var tooSoon = await ResendAsync(email);
        Assert.Equal(HttpStatusCode.Accepted, tooSoon.StatusCode);
        Assert.Single(await Outbox.OfAsync(fx, account), m => m.EventType == "GuestAccessRequested");

        fx.Clock.Advance(TimeSpan.FromMinutes(6));
        using var later = await ResendAsync(email.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.Accepted, later.StatusCode);
        Assert.Equal(2, (await Outbox.OfAsync(fx, account)).Count(m => m.EventType == "GuestAccessRequested"));

        using var nobody = await ResendAsync($"{Guid.NewGuid():N}@exemplo.com");
        Assert.Equal(HttpStatusCode.Accepted, nobody.StatusCode);
    }
}
