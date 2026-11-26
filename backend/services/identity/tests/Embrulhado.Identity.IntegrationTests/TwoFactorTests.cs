using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Embrulhado.BuildingBlocks.Testing;
using Embrulhado.Identity.Application;
using Embrulhado.Identity.Application.Accounts;
using Embrulhado.Identity.Domain;
using Embrulhado.Identity.Infrastructure;
using Embrulhado.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Embrulhado.Identity.IntegrationTests;

/// <summary>
/// Segundo fator obrigatório para o operador (ameaça T8): cadastro do autenticador no primeiro login, login em
/// dois passos, códigos de recuperação, bloqueio, tíquete de uso único e o claim amr no access token.
/// </summary>
public sealed class TwoFactorTests(IdentityFixture fx) : IClassFixture<IdentityFixture>
{
    private readonly IdentityClient _api = new(fx.Factory.CreateClient());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewEmail() => $"{Guid.NewGuid():N}@exemplo.com";

    private static string[] AmrOf(string accessToken) =>
        new JsonWebToken(accessToken).TryGetPayloadValue<string[]>("amr", out var amr) ? amr : [];

    private async Task<string> NewAdminWithoutTwoFactorAsync()
    {
        var email = NewEmail();
        using (var registered = await _api.RegisterAsync(email))
        {
            registered.EnsureSuccessStatusCode();
        }
        await fx.AddRoleAsync(email, Roles.Admin);
        return email;
    }

    private Task<int> FailedAttemptsAsync(string email) => fx.WithUserAsync(email, (_, u) => Task.FromResult(u.AccessFailedCount));

    [Fact]
    public async Task AdminWithoutTwoFactor_PasswordGivesOnlyASetupTicket_NeverASession()
    {
        var email = await NewAdminWithoutTwoFactorAsync();

        using var logins = new MetricRecorder(IdentityMetrics.MeterName, "embrulhado.identity.logins");
        using var login = await _api.LoginAsync(email);

        // a senha certa sem sessão não conta como login bem-sucedido
        Assert.True(logins.Total(("result", "mfa_setup_required")) >= 1);
        var challenge = await IdentityClient.ChallengeOf(login, "mfa_setup_required");
        var ticket = challenge.GetProperty("mfa_ticket").GetString()!;
        Assert.Equal(900, challenge.GetProperty("expires_in").GetInt32());
        // o tíquete do cadastro não é sessão: não renova, não entra no segundo passo do login
        using var asRefresh = await _api.RefreshAsync(ticket);
        Assert.Equal(HttpStatusCode.Unauthorized, asRefresh.StatusCode);
        using var asLogin = await _api.MfaAsync(ticket, "123456");
        Assert.Equal(HttpStatusCode.Unauthorized, asLogin.StatusCode);
        using var asBearer = await _api.MeAsync(ticket);
        Assert.Equal(HttpStatusCode.Unauthorized, asBearer.StatusCode);
    }

    [Fact]
    public async Task Setup_QrCodeThenFirstCode_EnablesTwoFactor_SessionWithAmr_TenHashedRecoveryCodes()
    {
        var email = await NewAdminWithoutTwoFactorAsync();
        using var login = await _api.LoginAsync(email);
        var ticket = await IdentityClient.TicketOf(login, "mfa_setup_required");

        using var setup = await _api.MfaSetupAsync(ticket);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        Assert.True(setup.Headers.CacheControl?.NoStore);
        var body = await IdentityClient.JsonOf(setup);
        ContractAssert.AssertSchema("identity.v1.yaml", "/components/schemas/MfaSetup", body.GetRawText());
        var key = body.GetProperty("shared_key").GetString()!;
        var uri = body.GetProperty("authenticator_uri").GetString()!;
        Assert.StartsWith($"otpauth://totp/Embrulhado:{Uri.EscapeDataString(email)}?secret=", uri, StringComparison.Ordinal);
        Assert.Contains("issuer=Embrulhado", uri, StringComparison.Ordinal);
        Assert.Contains("secret=" + key.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant(), uri, StringComparison.Ordinal);

        // código errado não liga a 2FA e conta como tentativa errada
        using var wrong = await _api.MfaConfirmAsync(ticket, "000000" == fx.CodeFor(key) ? "111111" : "000000");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
        Assert.Equal(1, await FailedAttemptsAsync(email));

        using var confirmed = await _api.MfaConfirmAsync(ticket, fx.CodeFor(key));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.True(confirmed.Headers.CacheControl?.NoStore);
        var enabled = await IdentityClient.JsonOf(confirmed);
        ContractAssert.AssertSchema("identity.v1.yaml", "/components/schemas/MfaEnabled", enabled.GetRawText());
        var codes = enabled.GetProperty("recovery_codes").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[0-9a-z]{5}-[0-9a-z]{5}$", c));
        var (access, _) = IdentityClient.Tokens(enabled);
        Assert.Equal(["mfa"], AmrOf(access));
        Assert.Equal(0, await FailedAttemptsAsync(email));

        using var me = await _api.MeAsync(access);
        var account = await IdentityClient.JsonOf(me);
        ContractAssert.AssertSchema("identity.v1.yaml", "/components/schemas/Account", account.GetRawText());
        Assert.True(account.GetProperty("two_factor_enabled").GetBoolean());
        Assert.Equal(10, account.GetProperty("recovery_codes_left").GetInt32());

        // o banco guarda só o hash dos códigos de recuperação
        var stored = await fx.WithUserAsync(email, async (users, u) =>
            await users.GetAuthenticationTokenAsync(u, "[AspNetUserStore]", "RecoveryCodes"));
        Assert.NotNull(stored);
        Assert.All(codes, c =>
        {
            Assert.DoesNotContain(c, stored, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(c.Replace("-", "", StringComparison.Ordinal), stored, StringComparison.OrdinalIgnoreCase);
        });

        // o tíquete foi gasto: nem o cadastro de novo, nem um segundo confirmar
        fx.NextCode();
        using var again = await _api.MfaConfirmAsync(ticket, fx.CodeFor(key));
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        using var setupAgain = await _api.MfaSetupAsync(ticket);
        Assert.Equal(HttpStatusCode.Unauthorized, setupAgain.StatusCode);
    }

    [Fact]
    public async Task LoginWithTwoFactor_WrongCodeCounts_RightCodeGivesSessionWithAmr_TicketAndCodeWorkOnce()
    {
        var admin = await _api.NewEnrolledAdminAsync(fx);

        using var logins = new MetricRecorder(IdentityMetrics.MeterName, "embrulhado.identity.logins");
        using var verifications = new MetricRecorder(IdentityMetrics.MeterName, "embrulhado.identity.mfa_verifications");
        using var login = await _api.LoginAsync(admin.Email);
        Assert.True(logins.Total(("result", "mfa_required")) >= 1);
        var challenge = await IdentityClient.ChallengeOf(login, "mfa_required");
        var ticket = challenge.GetProperty("mfa_ticket").GetString()!;
        Assert.Equal(300, challenge.GetProperty("expires_in").GetInt32());

        var right = fx.CodeFor(admin.SharedKey);
        using var wrong = await _api.MfaAsync(ticket, right == "000000" ? "111111" : "000000");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
        ContractAssert.AssertSchema("common/v1/common.yaml", "/components/schemas/ValidationProblem", (await IdentityClient.JsonOf(wrong)).GetRawText());
        Assert.Equal(1, await FailedAttemptsAsync(admin.Email));

        using var ok = await _api.MfaAsync(ticket, right[..3] + " " + right[3..]); // o espaço do app não atrapalha
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True(ok.Headers.CacheControl?.NoStore);
        var tokens = await IdentityClient.JsonOf(ok);
        ContractAssert.AssertSchema("identity.v1.yaml", "/components/schemas/Tokens", tokens.GetRawText());
        Assert.Equal(["mfa"], AmrOf(IdentityClient.Tokens(tokens).Access));
        Assert.Equal(0, await FailedAttemptsAsync(admin.Email));

        // o tíquete vale uma vez
        using var reused = await _api.MfaAsync(ticket, right);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        Assert.True(verifications.Total(("method", "totp"), ("result", "invalid_code")) >= 1);
        Assert.True(verifications.Total(("method", "totp"), ("result", "succeeded")) >= 1);
        Assert.True(verifications.Total(("method", "totp"), ("result", "invalid_ticket")) >= 1);

        // e o código também: outro login, no mesmo passo de 30 s, não entra com ele
        using var second = await _api.LoginAsync(admin.Email);
        var other = await IdentityClient.TicketOf(second, "mfa_required");
        using var replay = await _api.MfaAsync(other, right);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, replay.StatusCode);
    }

    [Fact]
    public async Task Code_FromTheStepBefore_IsAccepted_TwoStepsAway_IsNot()
    {
        var admin = await _api.NewEnrolledAdminAsync(fx);
        fx.NextCode();
        fx.NextCode();

        using var login = await _api.LoginAsync(admin.Email);
        var ticket = await IdentityClient.TicketOf(login, "mfa_required");
        using var tooOld = await _api.MfaAsync(ticket, fx.CodeFor(admin.SharedKey, steps: -2));
        using var tooNew = await _api.MfaAsync(ticket, fx.CodeFor(admin.SharedKey, steps: 2));
        using var late = await _api.MfaAsync(ticket, fx.CodeFor(admin.SharedKey, steps: -1)); // celular 30 s atrasado

        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooOld.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooNew.StatusCode);
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);
    }

    [Fact]
    public async Task RecoveryCode_OpensTheSession_OnlyOnce()
    {
        var admin = await _api.NewEnrolledAdminAsync(fx);
        var code = admin.RecoveryCodes[3];

        using var first = await _api.LoginAsync(admin.Email);
        using var used = await _api.MfaAsync(await IdentityClient.TicketOf(first, "mfa_required"), recoveryCode: code.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.OK, used.StatusCode);
        var (access, _) = IdentityClient.Tokens(await IdentityClient.JsonOf(used));
        Assert.Equal(["mfa"], AmrOf(access));
        using (var me = await _api.MeAsync(access))
        {
            Assert.Equal(9, (await IdentityClient.JsonOf(me)).GetProperty("recovery_codes_left").GetInt32());
        }

        using var second = await _api.LoginAsync(admin.Email);
        using var again = await _api.MfaAsync(await IdentityClient.TicketOf(second, "mfa_required"), recoveryCode: code);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);

        // os dois juntos, ou nenhum, é pedido malformado
        using var both = await _api.MfaAsync(await IdentityClient.TicketOf(await _api.LoginAsync(admin.Email), "mfa_required"), "123456", code);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, both.StatusCode);
    }

    [Fact]
    public async Task Ticket_ExpiresInFiveMinutes()
    {
        var admin = await _api.NewEnrolledAdminAsync(fx);
        using var login = await _api.LoginAsync(admin.Email);
        var ticket = await IdentityClient.TicketOf(login, "mfa_required");

        fx.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        using var late = await _api.MfaAsync(ticket, fx.CodeFor(admin.SharedKey));

        Assert.Equal(HttpStatusCode.Unauthorized, late.StatusCode);
        Assert.Equal(0, await FailedAttemptsAsync(admin.Email)); // tíquete vencido não chega a conferir o código
    }

    [Fact]
    public async Task FiveWrongCodes_LockTheAccount_EvenTheRightCodeWaits()
    {
        var admin = await _api.NewEnrolledAdminAsync(fx);
        using var login = await _api.LoginAsync(admin.Email);
        var ticket = await IdentityClient.TicketOf(login, "mfa_required");
        var right = fx.CodeFor(admin.SharedKey);
        var wrongCode = right == "000000" ? "111111" : "000000";

        for (var i = 0; i < 4; i++)
        {
            using var wrong = await _api.MfaAsync(ticket, wrongCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
        }
        using var fifth = await _api.MfaAsync(ticket, wrongCode);
        using var rightOne = await _api.MfaAsync(ticket, right);

        Assert.Equal(HttpStatusCode.Locked, fifth.StatusCode);
        Assert.Equal(HttpStatusCode.Locked, rightOne.StatusCode);
        Assert.NotNull(rightOne.Headers.RetryAfter);
    }

    [Fact]
    public async Task Refresh_KeepsAmr_OnlyInTheFamilyBornFromTheSecondFactor()
    {
        var admin = await _api.NewEnrolledAdminAsync(fx);

        using var refreshed = await _api.RefreshAsync(admin.Session.Refresh);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var (access, refresh) = IdentityClient.Tokens(await IdentityClient.JsonOf(refreshed));
        Assert.Equal(["mfa"], AmrOf(access));
        using var twice = await _api.RefreshAsync(refresh);
        Assert.Equal(["mfa"], AmrOf(IdentityClient.Tokens(await IdentityClient.JsonOf(twice)).Access));

        // a sessão de cliente não ganha amr no refresh
        var (customerAccess, customerRefresh) = await _api.NewSessionAsync();
        Assert.Empty(AmrOf(customerAccess));
        using var customer = await _api.RefreshAsync(customerRefresh);
        Assert.Empty(AmrOf(IdentityClient.Tokens(await IdentityClient.JsonOf(customer)).Access));
    }

    [Fact]
    public async Task SessionOpenedBeforeBecomingAdmin_DoesNotRefresh()
    {
        var email = NewEmail();
        var (_, refresh) = await _api.NewSessionAsync(email);
        await fx.AddRoleAsync(email, Roles.Admin);

        using var refreshed = await _api.RefreshAsync(refresh);

        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hash = RefreshTokenValue.Hash(refresh);
        var token = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == hash, Ct);
        Assert.NotNull(token.RevokedAt);
        Assert.Contains(token.RevocationReason, new RevocationReason?[] { RevocationReason.MfaRequired, RevocationReason.StampChanged });
    }

    [Fact]
    public async Task Customer_LogsInWithThePasswordAlone_NoAmr()
    {
        var (access, _) = await _api.NewSessionAsync();

        Assert.Empty(AmrOf(access));
    }

    [Fact]
    public async Task Auditor_IsAMachine_LogsInWithThePasswordAlone()
    {
        using var login = await _api.LoginAsync(IdentityFixture.AuditorEmail, IdentityFixture.AuditorPassword);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Empty(AmrOf(IdentityClient.Tokens(await IdentityClient.JsonOf(login)).Access));
    }

    [Fact]
    public async Task Google_ForAnAdmin_GoesThroughTheSameSecondFactor()
    {
        // o operador sem 2FA vincula o Google com a senha: sai o tíquete do cadastro, não a sessão
        var email = await NewAdminWithoutTwoFactorAsync();
        var sub = Guid.NewGuid().ToString("N");
        var lifetime = TimeSpan.FromHours(2); // os outros testes da classe adiantam o relógio do serviço
        using var link = await _api.LinkGoogleAsync(fx.Google.IdToken(sub, email, "n-1", expiresIn: lifetime), "n-1", "senha-boa-123");
        await IdentityClient.TicketOf(link, "mfa_setup_required");
        using var google = await _api.GoogleAsync(fx.Google.IdToken(sub, email, "n-2", expiresIn: lifetime), "n-2");
        await IdentityClient.TicketOf(google, "mfa_setup_required");

        // com a 2FA ativa: o Google é só o primeiro fator
        var admin = await _api.NewEnrolledAdminAsync(fx);
        var adminSub = Guid.NewGuid().ToString("N");
        using var linked = await _api.LinkGoogleAsync(fx.Google.IdToken(adminSub, admin.Email, "n-3", expiresIn: lifetime), "n-3", "senha-boa-123");
        await IdentityClient.TicketOf(linked, "mfa_required");
        using var viaGoogle = await _api.GoogleAsync(fx.Google.IdToken(adminSub, admin.Email, "n-4", expiresIn: lifetime), "n-4");
        var ticket = await IdentityClient.TicketOf(viaGoogle, "mfa_required");
        using var session = await _api.MfaAsync(ticket, fx.CodeFor(admin.SharedKey));
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Equal(["mfa"], AmrOf(IdentityClient.Tokens(await IdentityClient.JsonOf(session)).Access));
    }

    [Fact]
    public async Task PasswordReset_KeepsTwoFactorOn()
    {
        var admin = await _api.NewEnrolledAdminAsync(fx);
        var (id, token) = await fx.WithUserAsync(admin.Email, async (users, u) => (u.Id, await users.GeneratePasswordResetTokenAsync(u)));
        var encoded = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(token));

        using var reset = await _api.Http.PostAsJsonAsync("/v1/accounts/password-reset/confirm",
            new { account_id = id, token = encoded, password = "senha-nova-456" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.True(await fx.WithUserAsync(admin.Email, (users, u) => users.GetTwoFactorEnabledAsync(u)));
        using var login = await _api.LoginAsync(admin.Email, "senha-nova-456");
        var ticket = await IdentityClient.TicketOf(login, "mfa_required"); // não volta para o cadastro
        using var session = await _api.MfaAsync(ticket, fx.CodeFor(admin.SharedKey)); // a mesma chave de antes
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    }

    [Fact]
    public async Task TicketFromBeforeAPasswordChange_NoLongerWorks()
    {
        var admin = await _api.NewEnrolledAdminAsync(fx);
        using var login = await _api.LoginAsync(admin.Email);
        var ticket = await IdentityClient.TicketOf(login, "mfa_required");

        await fx.WithUserAsync(admin.Email, async (users, u) => (await users.UpdateSecurityStampAsync(u)).Succeeded);
        using var late = await _api.MfaAsync(ticket, fx.CodeFor(admin.SharedKey));

        Assert.Equal(HttpStatusCode.Unauthorized, late.StatusCode);
    }

    [Fact]
    public async Task RegenerateRecoveryCodes_NeedsTheCurrentCode_OldCodesStopWorking()
    {
        var admin = await _api.NewEnrolledAdminAsync(fx);

        var right = fx.CodeFor(admin.SharedKey);
        using var wrong = await _api.RegenerateRecoveryCodesAsync(admin.Session.Access, right == "000000" ? "111111" : "000000");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);

        using var regenerated = await _api.RegenerateRecoveryCodesAsync(admin.Session.Access, right);
        Assert.Equal(HttpStatusCode.OK, regenerated.StatusCode);
        var body = await IdentityClient.JsonOf(regenerated);
        ContractAssert.AssertSchema("identity.v1.yaml", "/components/schemas/RecoveryCodes", body.GetRawText());
        var fresh = body.GetProperty("recovery_codes").EnumerateArray().Select(c => c.GetString()!).ToList();
        Assert.Equal(10, fresh.Count);
        Assert.Empty(fresh.Intersect(admin.RecoveryCodes));

        using var login = await _api.LoginAsync(admin.Email);
        var ticket = await IdentityClient.TicketOf(login, "mfa_required");
        using var old = await _api.MfaAsync(ticket, recoveryCode: admin.RecoveryCodes[0]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, old.StatusCode);
        using var ok = await _api.MfaAsync(ticket, recoveryCode: fresh[0]);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task RegenerateRecoveryCodes_WithoutTwoFactor_IsRefused()
    {
        var (access, _) = await _api.NewSessionAsync(); // cliente: sessão sem amr

        using var response = await _api.RegenerateRecoveryCodesAsync(access, "123456");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ambiente local e CI: a chave fixa da configuração liga a 2FA do operador, para os scripts entrarem sem celular
    [Fact]
    public async Task Seeder_TotpKeyFromConfiguration_EnablesTwoFactor_OnlyWhereItIsOff()
    {
        const string key = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
        var email = await NewAdminWithoutTwoFactorAsync();
        var enrolled = await _api.NewEnrolledAdminAsync(fx);

        foreach (var who in new[] { email, enrolled.Email })
        {
            await using var scope = fx.Factory.Services.CreateAsyncScope();
            var seeder = new AdminSeeder(
                scope.ServiceProvider.GetRequiredService<UserManager<User>>(), scope.ServiceProvider.GetRequiredService<IUserStore<User>>(),
                Options.Create(new AdminSeedOptions { Email = who, Password = "senha-boa-123", TotpKey = key }),
                Options.Create(new AuditorSeedOptions()), fx.Clock, NullLogger<AdminSeeder>.Instance);
            await seeder.SeedAsync();
        }

        using var login = await _api.LoginAsync(email);
        using var session = await _api.MfaAsync(await IdentityClient.TicketOf(login, "mfa_required"), fx.CodeFor(key));
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        // quem já tinha o próprio app continua com ele
        fx.NextCode();
        using var other = await _api.LoginAsync(enrolled.Email);
        using var withOwn = await _api.MfaAsync(await IdentityClient.TicketOf(other, "mfa_required"), fx.CodeFor(enrolled.SharedKey));
        Assert.Equal(HttpStatusCode.OK, withOwn.StatusCode);
    }

    [Theory]
    [InlineData("CURTA")]
    [InlineData("nao-e-base32-!!!!!!!!!!!!!!!!!!!!")]
    public async Task Seeder_TotpKeyTooShortOrInvalid_FailsAtStartup(string key)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var seeder = new AdminSeeder(
            scope.ServiceProvider.GetRequiredService<UserManager<User>>(), scope.ServiceProvider.GetRequiredService<IUserStore<User>>(),
            Options.Create(new AdminSeedOptions { Email = IdentityFixture.AdminEmail, Password = IdentityFixture.AdminPassword, TotpKey = key }),
            Options.Create(new AuditorSeedOptions()), fx.Clock, NullLogger<AdminSeeder>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(seeder.SeedAsync);
    }

    // o procedimento de deploy/README.md para quem perdeu o celular e os códigos de recuperação
    [Fact]
    public async Task OperatorSqlProcedure_TurnsTwoFactorOff_TheNextLoginEnrollsAgain()
    {
        var admin = await _api.NewEnrolledAdminAsync(fx);
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var email = admin.Email;
            await db.Database.ExecuteSqlAsync($"""
                UPDATE users SET two_factor_enabled = false, security_stamp = gen_random_uuid()::text,
                       access_failed_count = 0, lockout_end = NULL
                 WHERE normalized_email = upper({email});
                """, Ct);
            await db.Database.ExecuteSqlAsync($"""
                DELETE FROM user_tokens
                 WHERE user_id = (SELECT id FROM users WHERE normalized_email = upper({email}))
                   AND login_provider IN ('[AspNetUserStore]', '[EmbrulhadoTotp]');
                """, Ct);
            await db.Database.ExecuteSqlAsync($"""
                UPDATE refresh_tokens SET revoked_at = now(), revocation_reason = 'MfaRequired'
                 WHERE user_id = (SELECT id FROM users WHERE normalized_email = upper({email}))
                   AND revoked_at IS NULL;
                """, Ct);
        }

        using var refreshed = await _api.RefreshAsync(admin.Session.Refresh);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
        using var login = await _api.LoginAsync(admin.Email);
        var ticket = await IdentityClient.TicketOf(login, "mfa_setup_required");
        using var setup = await _api.MfaSetupAsync(ticket);
        var key = (await IdentityClient.JsonOf(setup)).GetProperty("shared_key").GetString()!;
        Assert.NotEqual(admin.SharedKey, key);
        using var enabled = await _api.MfaConfirmAsync(ticket, fx.CodeFor(key));
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
    }

    [Fact]
    public async Task Ticket_Garbage_Is401_Not500()
    {
        using var empty = await _api.MfaAsync("", "123456");
        using var unknown = await _api.MfaAsync(RefreshTokenValue.New(), "123456");
        using var huge = await _api.MfaSetupAsync(new string('a', 4000));

        Assert.Equal(HttpStatusCode.Unauthorized, empty.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, huge.StatusCode);
    }
}
