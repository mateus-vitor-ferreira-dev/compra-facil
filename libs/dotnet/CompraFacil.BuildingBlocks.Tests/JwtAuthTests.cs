using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CompraFacil.BuildingBlocks.Tests;

/// <summary>Os tokens do identity nas rotas protegidas: RS256 pelo JWKS, emissor, audiência, validade e papel.</summary>
public sealed class JwtAuthTests : IAsyncLifetime
{
    private readonly TestIdentity _identity = new();
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async ValueTask InitializeAsync()
    {
        _app = App(new() { ["Auth:Jwks"] = _identity.Jwks });
        _app.MapPost("/v1/admin/x", (ClaimsPrincipal user) => Results.Ok(new { sub = user.Identity!.Name }))
            .RequireAuthorization(JwtAuth.AdminPolicy);
        _app.MapPost("/v1/me/x", (ClaimsPrincipal user) => Results.Ok(new { email = user.FindFirstValue("email") }))
            .RequireAuthorization(JwtAuth.CustomerPolicy);
        await _app.StartAsync();
        _http = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
        _identity.Dispose();
    }

    private static WebApplication App(Dictionary<string, string?> settings, string environment = "Development")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        foreach (var (key, value) in settings)
        {
            builder.Configuration[key] = value;
        }
        builder.AddServiceDefaults(new ServiceInfo("inventory"));
        builder.AddJwtAuth();
        var app = builder.Build();
        app.UseServiceDefaults();
        return app;
    }

    private async Task<HttpResponseMessage> CallAsync(string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await _http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AdminToken_PassesTheAdminRoute()
    {
        using var response = await CallAsync("/v1/admin/x", _identity.AdminToken());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("0199f6a0-0000-7000-8000-000000000a1a", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomerToken_PassesTheCustomerRoute_WithTheEmailClaim()
    {
        using var response = await CallAsync("/v1/me/x", _identity.CustomerToken(email: "bia@exemplo.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("bia@exemplo.com", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    public static TheoryData<string, Func<TestIdentity, string?>> Unauthorized => new()
    {
        { "sem token", _ => null },
        { "outro emissor", i => i.Token(["admin"], issuer: "terceiro") },
        { "outra audiência", i => i.Token(["admin"], audience: "inventory") },
        { "expirado", i => i.Token(["admin"], expires: DateTime.UtcNow.AddMinutes(-5)) },
        { "assinado por outra chave (mesmo formato)", _ => Forged() },
        { "o antigo JWT de dev (HS256)", _ => DevHs256() },
    };

    [Theory]
    [MemberData(nameof(Unauthorized))]
    public async Task InvalidToken_Is401Problem(string because, Func<TestIdentity, string?> token)
    {
        ArgumentNullException.ThrowIfNull(token);
        using var response = await CallAsync("/v1/admin/x", token(_identity));
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, because);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/v1/admin/x", "customer")] // cliente na rota de operador
    [InlineData("/v1/me/x", "admin")]       // operador não compra em nome de ninguém
    public async Task TokenWithoutTheRole_Is403Problem(string path, string role)
    {
        using var response = await CallAsync(path, _identity.Token([role]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        Assert.Equal("Sem permissão", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task TokenWithoutSubject_Is403()
    {
        using var response = await CallAsync("/v1/admin/x", _identity.Token(["admin"], subject: null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("identity:8006")]
    [InlineData("ftp://identity")]
    public void Authority_Invalid_FailsAtStartup(string? authority)
    {
        var error = Assert.Throws<InvalidOperationException>(() => App(new() { ["Auth:Authority"] = authority }));
        Assert.Contains("Auth:Authority", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Authority_WithoutHttps_OutsideDevelopment_FailsUnlessAllowed()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            App(new() { ["Auth:Authority"] = "http://identity:8006" }, environment: "Production"));
        Assert.Contains("Auth__RequireHttpsMetadata=false", error.Message, StringComparison.Ordinal);

        using var allowed = App(new() { ["Auth:Authority"] = "http://identity:8006", ["Auth:RequireHttpsMetadata"] = "false" }, "Production");
    }

    private static string Forged()
    {
        using var other = new TestIdentity();
        return other.AdminToken();
    }

    private static string DevHs256() => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = TestIdentity.Issuer,
        Audience = TestIdentity.Audience,
        Claims = new Dictionary<string, object> { ["roles"] = "admin", ["sub"] = "ana" },
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes("dev-only-compra-facil-jwt-secret-troque-em-producao")), SecurityAlgorithms.HmacSha256),
    });
}

/// <summary>O caminho de produção: descoberta + JWKS por HTTP, e a chave nova da rotação aceita sem reiniciar.</summary>
public sealed class JwtAuthDiscoveryTests : IAsyncLifetime
{
    private TestIdentity _current = new();
    private WebApplication _identity = null!;
    private WebApplication _service = null!;
    private HttpClient _http = null!;

    public async ValueTask InitializeAsync()
    {
        // um identity de mentira, servindo descoberta e JWKS por HTTP de verdade (porta livre)
        var ib = WebApplication.CreateBuilder();
        ib.WebHost.UseUrls("http://127.0.0.1:0");
        _identity = ib.Build();
        _identity.MapGet("/.well-known/openid-configuration", (HttpRequest r) => Results.Json(new
        {
            issuer = TestIdentity.Issuer,
            jwks_uri = $"{r.Scheme}://{r.Host}/.well-known/jwks.json",
        }));
        _identity.MapGet("/.well-known/jwks.json", () => Results.Text(_current.Jwks, "application/json"));
        await _identity.StartAsync();

        var sb = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        sb.WebHost.UseTestServer();
        sb.Configuration["Auth:Authority"] = _identity.Services.GetRequiredService<IServer>().Features
            .GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        sb.Configuration["Auth:KeyRefreshInterval"] = "00:00:01";
        sb.AddServiceDefaults(new ServiceInfo("orders"));
        sb.AddJwtAuth();
        _service = sb.Build();
        _service.UseServiceDefaults();
        _service.MapGet("/v1/admin/x", () => Results.Ok()).RequireAuthorization(JwtAuth.AdminPolicy);
        await _service.StartAsync();
        _http = _service.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _service.DisposeAsync();
        await _identity.DisposeAsync();
        _current.Dispose();
    }

    private async Task<HttpStatusCode> CallAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/admin/x");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    [Fact]
    public async Task ValidatesByTheIdentityJwks_AndLearnsTheRotatedKey()
    {
        Assert.Equal(HttpStatusCode.OK, await CallAsync(_current.AdminToken()));

        // o identity trocou de chave sem anunciar antes (rotação atrasada): o JwtBearer recusa o token
        // com kid desconhecido e pede o JWKS de novo em segundo plano; os seguintes passam
        var old = _current;
        _current = new TestIdentity();
        old.Dispose();
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken); // KeyRefreshInterval entre buscas

        var token = _current.AdminToken();
        var status = HttpStatusCode.Unauthorized;
        for (var i = 0; i < 50 && status != HttpStatusCode.OK; i++)
        {
            status = await CallAsync(token);
            if (status != HttpStatusCode.OK)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
        }
        Assert.Equal(HttpStatusCode.OK, status);
    }
}
