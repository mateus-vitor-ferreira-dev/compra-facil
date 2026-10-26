using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Identity.IntegrationTests;

/// <summary>Os eventos de conta no outbox, com o contrato do AsyncAPI.</summary>
public static class Outbox
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<List<OutboxMessage>> OfAsync(IdentityFixture fx, Guid? accountId = null)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Set<OutboxMessage>().AsNoTracking();
        if (accountId is { } id)
        {
            query = query.Where(m => m.MessageKey == id.ToString());
        }
        var messages = await query.OrderBy(m => m.Id).ToListAsync(Ct);
        foreach (var m in messages)
        {
            ContractAssert.AssertEvent("identity.v1.yaml", m.EventType, m.Payload);
        }
        return messages;
    }

    /// <summary>(conta, token) do último link de confirmação, como o front os tira da URL.</summary>
    public static async Task<(Guid Account, string Token)> LastLinkAsync(IdentityFixture fx, Guid accountId)
    {
        var request = (await OfAsync(fx, accountId)).Last(m => m.EventType == "EmailConfirmationRequested");
        var url = new Uri(EventEnvelope.TryParse(request.Payload)!.Data.GetProperty("confirmation_url").GetString()!);
        var query = HttpUtility.ParseQueryString(url.Query);
        return (Guid.Parse(query["conta"]!), query["token"]!);
    }
}

public sealed class EmailConfirmationTests(IdentityFixture fx) : IClassFixture<IdentityFixture>
{
    private readonly IdentityClient _api = new(fx.Factory.CreateClient());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> RegisterAsync(string email)
    {
        using var response = await _api.RegisterAsync(email);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await IdentityClient.JsonOf(response)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> ConfirmAsync(Guid account, string token) =>
        _api.Http.PostAsJsonAsync("/v1/accounts/confirm-email", new { account_id = account, token }, Ct);

    private Task<HttpResponseMessage> ResendAsync(string email) =>
        _api.Http.PostAsJsonAsync("/v1/accounts/confirm-email/resend", new { email }, Ct);

    [Fact]
    public async Task Register_PublishesAccountRegistered_AndTheConfirmationRequest_InTheSameTransaction()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        var account = await RegisterAsync(email);

        var events = await Outbox.OfAsync(fx, account);

        Assert.Equal(["AccountRegistered", "EmailConfirmationRequested"], events.Select(e => e.EventType));
        Assert.All(events, e => Assert.Equal("identity.account.v1", e.Topic));
        var registered = EventEnvelope.TryParse(events[0].Payload)!.Data;
        Assert.Equal(email, registered.GetProperty("email").GetString());
        var link = await Outbox.LastLinkAsync(fx, account);
        Assert.Equal(account, link.Account);
        Assert.StartsWith("http://localhost:3000/conta/confirmar-email?", EventEnvelope.TryParse(events[1].Payload)!.Data
            .GetProperty("confirmation_url").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedRegistration_PublishesNothing()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        await RegisterAsync(email);
        var before = (await Outbox.OfAsync(fx)).Count;

        using var duplicate = await _api.RegisterAsync(email.ToUpperInvariant());
        using var weak = await _api.RegisterAsync($"{Guid.NewGuid():N}@exemplo.com", "aaaabbbb");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, weak.StatusCode);
        Assert.Equal(before, (await Outbox.OfAsync(fx)).Count);
    }

    [Fact]
    public async Task ConfirmEmail_WithTheLinkToken_MarksTheAccount_AndIsIdempotent()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        var account = await RegisterAsync(email);
        var (_, token) = await Outbox.LastLinkAsync(fx, account);

        using var confirmed = await ConfirmAsync(account, token);
        using var again = await ConfirmAsync(account, token);

        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        using var login = await _api.LoginAsync(email);
        using var me = await _api.MeAsync(IdentityClient.Tokens(await IdentityClient.JsonOf(login)).Access);
        Assert.True((await IdentityClient.JsonOf(me)).GetProperty("email_confirmed").GetBoolean());
    }

    [Fact]
    public async Task ConfirmEmail_WrongTokenOrUnknownAccount_SameGeneric422()
    {
        var account = await RegisterAsync($"{Guid.NewGuid():N}@exemplo.com");
        var other = await RegisterAsync($"{Guid.NewGuid():N}@exemplo.com");
        var (_, otherToken) = await Outbox.LastLinkAsync(fx, other);

        using var wrong = await ConfirmAsync(account, otherToken); // token de outra conta
        using var garbage = await ConfirmAsync(account, "nao-e-um-token");
        using var unknown = await ConfirmAsync(Guid.NewGuid(), otherToken);

        foreach (var r in new[] { wrong, garbage, unknown })
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            var problem = await r.Content.ReadAsStringAsync(Ct);
            ContractAssert.AssertSchema("common/v1/common.yaml", "/components/schemas/ValidationProblem", problem);
            Assert.Contains("\"field\":\"token\"", problem, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Resend_IsAlways202_ButOnlyPublishesForAnUnconfirmedAccount()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        var account = await RegisterAsync(email);

        using var resend = await ResendAsync(email);
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        Assert.Equal(2, (await Outbox.OfAsync(fx, account)).Count(e => e.EventType == "EmailConfirmationRequested"));

        var before = (await Outbox.OfAsync(fx)).Count;
        using var unknown = await ResendAsync($"{Guid.NewGuid():N}@exemplo.com");
        var (_, token) = await Outbox.LastLinkAsync(fx, account);
        using (await ConfirmAsync(account, token))
        {
        }
        using var confirmed = await ResendAsync(email);

        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, confirmed.StatusCode);
        Assert.Equal(before, (await Outbox.OfAsync(fx)).Count);
    }
}

public sealed class RequiredConfirmationFixture : IdentityFixture
{
    protected override bool RequireConfirmedEmail => true;
}

/// <summary>Com Identity:EmailConfirmation:Required, o login espera a confirmação.</summary>
public sealed class RequiredConfirmationTests(RequiredConfirmationFixture fx) : IClassFixture<RequiredConfirmationFixture>
{
    private readonly IdentityClient _api = new(fx.Factory.CreateClient());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_BeforeConfirming_Is403_AndWorksAfter()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        using var registered = await _api.RegisterAsync(email);
        var account = (await IdentityClient.JsonOf(registered)).GetProperty("id").GetGuid();

        using var before = await _api.LoginAsync(email);
        using var wrongPassword = await _api.LoginAsync(email, "senha-errada-1"); // a senha errada continua 401
        var (_, token) = await Outbox.LastLinkAsync(fx, account);
        using (await _api.Http.PostAsJsonAsync("/v1/accounts/confirm-email", new { account_id = account, token }, Ct))
        {
        }
        using var after = await _api.LoginAsync(email);

        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);
        Assert.Equal("E-mail não confirmado", (await IdentityClient.JsonOf(before)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task UnconfirmedAccount_WrongPasswordsStillLockIt()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
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

        Assert.Equal(HttpStatusCode.Locked, fifth.StatusCode); // a quinta bloqueia, como no login normal
        Assert.Equal(HttpStatusCode.Locked, right.StatusCode); // e nem a senha certa entra
    }

    [Fact]
    public async Task TheConfiguredOperator_LogsInWithoutConfirming()
    {
        using var login = await _api.LoginAsync(IdentityFixture.AdminEmail, IdentityFixture.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
