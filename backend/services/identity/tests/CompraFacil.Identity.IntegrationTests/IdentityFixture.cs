using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Identity.Application.Accounts;
using CompraFacil.Identity.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace CompraFacil.Identity.IntegrationTests;

/// <summary>
/// O identity com um Postgres real e um relógio controlado: os testes avançam o tempo para ver token
/// vencer e chave rotacionar. Um por classe de teste (o tempo de uma classe não vaza para outra).
/// </summary>
public class IdentityFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@comprafacil.test";
    public const string AdminPassword = "senha-do-operador";
    public const string AuditorEmail = "leitura@comprafacil.test";
    public const string AuditorPassword = "senha-da-leitura";

    private readonly PostgresFixture _postgres = new();

    // começa em microssegundos redondos (precisão do timestamptz): escrita e leitura dão a mesma data
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    /// <summary>O Google de mentira que emite os ID tokens do login com o Google.</summary>
    public FakeGoogle Google { get; } = new();

    /// <summary>Login só com e-mail confirmado (Identity:EmailConfirmation:Required).</summary>
    protected virtual bool RequireConfirmedEmail => false;

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _postgres.ConnectionString)
            .UseSetting("Identity:Admin:Email", AdminEmail)
            .UseSetting("Identity:Admin:Password", AdminPassword)
            .UseSetting("Identity:Auditor:Email", AuditorEmail)
            .UseSetting("Identity:Auditor:Password", AuditorPassword)
            .UseSetting("Kafka:Enabled", "false") // os eventos são conferidos no outbox
            .UseSetting("Identity:EmailConfirmation:Required", RequireConfirmedEmail ? "true" : "false")
            .UseSetting("Identity:Google:Authority", FakeGoogle.Authority)
            .UseSetting("Identity:Google:ClientIds:0", FakeGoogle.ClientId)
            .ConfigureTestServices(s =>
            {
                s.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
                s.AddHttpClient("google").ConfigurePrimaryHttpMessageHandler(Google.Handler);
            }));
        using var _ = Factory.CreateClient(); // sobe: migrations, primeira chave e admin
    }

    /// <summary>Dá um papel a uma conta já cadastrada (o operador nasce da configuração; os testes criam outros).</summary>
    public async Task AddRoleAsync(string email, string role)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException($"{email} não existe");
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
    }

    /// <summary>A conta como está no banco agora.</summary>
    public async Task<T> WithUserAsync<T>(string email, Func<UserManager<User>, User, Task<T>> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException($"{email} não existe");
        return await read(users, user);
    }

    /// <summary>O código que o app autenticador mostraria agora (ou <paramref name="steps"/> passos antes/depois).</summary>
    public string CodeFor(string sharedKey, int steps = 0) =>
        Totp.Code(Totp.FromBase32(sharedKey), Totp.StepAt(Clock.GetUtcNow()) + steps);

    /// <summary>Espera o próximo código do autenticador (o mesmo código não vale duas vezes).</summary>
    public void NextCode() => Clock.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
        Google.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Chamadas da API como o front faria.</summary>
public sealed class IdentityClient(HttpClient http)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HttpClient Http => http;

    public Task<HttpResponseMessage> RegisterAsync(string email, string password = "senha-boa-123", string name = "Ana Souza") =>
        http.PostAsJsonAsync("/v1/accounts", new { email, password, name }, Ct);

    public Task<HttpResponseMessage> LoginAsync(string email, string password = "senha-boa-123") =>
        http.PostAsJsonAsync("/v1/sessions", new { email, password }, Ct);

    public Task<HttpResponseMessage> GoogleAsync(string idToken, string nonce) =>
        http.PostAsJsonAsync("/v1/sessions/google", new { id_token = idToken, nonce }, Ct);

    public Task<HttpResponseMessage> LinkGoogleAsync(string idToken, string nonce, string password) =>
        http.PostAsJsonAsync("/v1/sessions/google/link", new { id_token = idToken, nonce, password }, Ct);

    public Task<HttpResponseMessage> MfaAsync(string ticket, string? code = null, string? recoveryCode = null) =>
        http.PostAsJsonAsync("/v1/sessions/mfa", new { ticket, code, recovery_code = recoveryCode }, Ct);

    public Task<HttpResponseMessage> MfaSetupAsync(string ticket) =>
        http.PostAsJsonAsync("/v1/sessions/mfa/setup", new { ticket }, Ct);

    public Task<HttpResponseMessage> MfaConfirmAsync(string ticket, string code) =>
        http.PostAsJsonAsync("/v1/sessions/mfa/setup/confirm", new { ticket, code }, Ct);

    public async Task<HttpResponseMessage> RegenerateRecoveryCodesAsync(string accessToken, string code)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/me/two-factor/recovery-codes")
        {
            Content = JsonContent.Create(new { code }),
        };
        request.Headers.Authorization = new("Bearer", accessToken);
        return await http.SendAsync(request, Ct);
    }

    /// <summary>O tíquete do 202 do login (senha ou Google), conferindo para que ele serve.</summary>
    public static async Task<string> TicketOf(HttpResponseMessage response, string status) =>
        (await ChallengeOf(response, status)).GetProperty("mfa_ticket").GetString()!;

    /// <summary>O corpo do 202 do login, conferido contra o contrato.</summary>
    public static async Task<JsonElement> ChallengeOf(HttpResponseMessage response, string status)
    {
        ArgumentNullException.ThrowIfNull(response);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await JsonOf(response);
        ContractAssert.AssertSchema("identity.v1.yaml", "/components/schemas/MfaChallenge", body.GetRawText());
        Assert.Equal(status, body.GetProperty("status").GetString());
        Assert.False(body.TryGetProperty("access_token", out _));
        return body;
    }

    /// <summary>
    /// Cadastra uma conta, dá o papel de operador e faz o cadastro do autenticador pelo login: (e-mail, chave,
    /// tokens da sessão com amr, códigos de recuperação).
    /// </summary>
    public async Task<EnrolledAdmin> NewEnrolledAdminAsync(IdentityFixture fx)
    {
        ArgumentNullException.ThrowIfNull(fx);
        var email = $"operador-{Guid.NewGuid():N}@exemplo.com";
        using (var registered = await RegisterAsync(email))
        {
            registered.EnsureSuccessStatusCode();
        }
        await fx.AddRoleAsync(email, Roles.Admin);
        using var login = await LoginAsync(email);
        var ticket = await TicketOf(login, "mfa_setup_required");
        using var setup = await MfaSetupAsync(ticket);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var key = (await JsonOf(setup)).GetProperty("shared_key").GetString()!;
        using var confirmed = await MfaConfirmAsync(ticket, fx.CodeFor(key));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var body = await JsonOf(confirmed);
        fx.NextCode(); // o código do cadastro já foi usado
        return new EnrolledAdmin(email, key, Tokens(body),
            [.. body.GetProperty("recovery_codes").EnumerateArray().Select(c => c.GetString()!)]);
    }

    public Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        http.PostAsJsonAsync("/v1/sessions/refresh", new { refresh_token = refreshToken }, Ct);

    public Task<HttpResponseMessage> RevokeAsync(string refreshToken) =>
        http.PostAsJsonAsync("/v1/sessions/revoke", new { refresh_token = refreshToken }, Ct);

    public async Task<HttpResponseMessage> MeAsync(string? accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/me");
        if (accessToken is not null)
        {
            request.Headers.Authorization = new("Bearer", accessToken);
        }
        return await http.SendAsync(request, Ct);
    }

    /// <summary>Cadastra uma conta nova e faz login: (access token, refresh token).</summary>
    public async Task<(string Access, string Refresh)> NewSessionAsync(string? email = null)
    {
        email ??= $"{Guid.NewGuid():N}@exemplo.com";
        using (var registered = await RegisterAsync(email))
        {
            registered.EnsureSuccessStatusCode();
        }
        using var login = await LoginAsync(email);
        login.EnsureSuccessStatusCode();
        return Tokens(await JsonOf(login));
    }

    public static (string Access, string Refresh) Tokens(JsonElement body) =>
        (body.GetProperty("access_token").GetString()!, body.GetProperty("refresh_token").GetString()!);

    public static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
}

/// <summary>Um operador com a 2FA ativa: a chave do autenticador, a sessão do cadastro e os códigos de recuperação.</summary>
public sealed record EnrolledAdmin(string Email, string SharedKey, (string Access, string Refresh) Session, IReadOnlyList<string> RecoveryCodes);
