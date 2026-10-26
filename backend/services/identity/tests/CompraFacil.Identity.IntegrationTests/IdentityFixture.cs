using System.Net.Http.Json;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Testing;
using Microsoft.AspNetCore.Hosting;
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

    private readonly PostgresFixture _postgres = new();

    // começa em microssegundos redondos (precisão do timestamptz): escrita e leitura dão a mesma data
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    /// <summary>Login só com e-mail confirmado (Identity:EmailConfirmation:Required).</summary>
    protected virtual bool RequireConfirmedEmail => false;

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _postgres.ConnectionString)
            .UseSetting("Identity:Admin:Email", AdminEmail)
            .UseSetting("Identity:Admin:Password", AdminPassword)
            .UseSetting("Kafka:Enabled", "false") // os eventos são conferidos no outbox
            .UseSetting("Identity:EmailConfirmation:Required", RequireConfirmedEmail ? "true" : "false")
            .ConfigureTestServices(s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock))));
        using var _ = Factory.CreateClient(); // sobe: migrations, primeira chave e admin
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
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
