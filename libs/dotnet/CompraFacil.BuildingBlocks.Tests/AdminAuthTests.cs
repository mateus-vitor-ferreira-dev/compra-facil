using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CompraFacil.BuildingBlocks.Tests;

/// <summary>O mesmo JWT do catalog (HS256, iss, aud = serviço, roles, sub, exp).</summary>
public sealed class AdminAuthTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.AddServiceDefaults(new ServiceInfo("inventory"));
        builder.AddAdminAuth(audience: "inventory");
        _app = builder.Build();
        _app.UseServiceDefaults();
        _app.MapPost("/v1/admin/x", (ClaimsPrincipal user) => Results.Ok(new { sub = user.Identity!.Name }))
            .RequireAuthorization(AdminAuth.Policy);
        await _app.StartAsync();
        _http = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }

    private static string Token(
        string audience = "inventory", string[]? roles = null, string? subject = "ana",
        string secret = AdminAuth.DevJwtSecret, string algorithm = SecurityAlgorithms.HmacSha256, DateTime? expires = null)
    {
        var claims = new Dictionary<string, object> { ["roles"] = roles ?? ["admin"] };
        if (subject is not null)
        {
            claims["sub"] = subject;
        }
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "compra-facil",
            Audience = audience,
            Claims = claims,
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), algorithm),
        });
    }

    private async Task<HttpResponseMessage> CallAsync(string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/x");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await _http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ValidAdminToken_Passes()
    {
        using var response = await CallAsync(Token());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"sub\":\"ana\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    public static TheoryData<string, string?> Unauthorized => new()
    {
        { "sem token", null },
        { "outra audiência (token do catalog)", Token(audience: "catalog") },
        { "outro segredo", Token(secret: new string('x', 40)) },
        { "expirado", Token(expires: DateTime.UtcNow.AddHours(-1)) },
        { "outro algoritmo", Token(secret: new string('y', 64), algorithm: SecurityAlgorithms.HmacSha512) },
    };

    [Theory]
    [MemberData(nameof(Unauthorized))]
    public async Task InvalidToken_Is401Problem(string because, string? token)
    {
        using var response = await CallAsync(token);
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, because);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task TokenWithoutAdminRole_Is403Problem()
    {
        using var response = await CallAsync(Token(roles: ["viewer"]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        Assert.Equal("Sem permissão", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task TokenWithoutSubject_Is403()
    {
        using var response = await CallAsync(Token(subject: null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public void DevSecret_IsRefusedOutsideDevelopment()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        var error = Assert.Throws<InvalidOperationException>(() => builder.AddAdminAuth("inventory"));
        Assert.Contains("Auth__JwtSecret", error.Message, StringComparison.Ordinal);
    }
}
