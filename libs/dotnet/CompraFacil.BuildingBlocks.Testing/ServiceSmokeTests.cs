using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CompraFacil.BuildingBlocks.Testing;

/// <summary>
/// Smoke test comum dos serviços .NET: o serviço sobe com o Postgres dele, aplica as migrations,
/// responde /health e /ready, serve o contrato em /docs e devolve erros em ProblemDetails.
/// Cada serviço herda com uma linha (ver tests/*.IntegrationTests).
/// </summary>
/// <typeparam name="TProgram">O Program da Api do serviço.</typeparam>
public abstract class ServiceSmokeTests<TProgram> : IClassFixture<PostgresFixture>, IAsyncLifetime
    where TProgram : class
{
    private readonly WebApplicationFactory<TProgram> _factory;
    private HttpClient _http = null!;

    protected ServiceSmokeTests(PostgresFixture postgres)
    {
        ArgumentNullException.ThrowIfNull(postgres);
        _factory = new WebApplicationFactory<TProgram>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:Default", postgres.ConnectionString));
    }

    /// <summary>Arquivo do contrato em contracts/openapi; nulo se o serviço não tem API pública.</summary>
    protected abstract string? ContractFile { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _http = _factory.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Health_IsUp()
    {
        using var response = await _http.GetAsync(new Uri("/health", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_ReachesPostgres()
    {
        using var response = await _http.GetAsync(new Uri("/ready", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Docs_ServeTheContract()
    {
        using var docs = await _http.GetAsync(new Uri("/docs", UriKind.Relative), Ct);
        if (ContractFile is null)
        {
            Assert.Equal(HttpStatusCode.NotFound, docs.StatusCode);
            return;
        }

        Assert.Equal(HttpStatusCode.OK, docs.StatusCode);
        using var contract = await _http.GetAsync(new Uri($"/openapi/{ContractFile}", UriKind.Relative), Ct);
        using var common = await _http.GetAsync(new Uri("/openapi/common/v1/common.yaml", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, contract.StatusCode);
        Assert.Equal(HttpStatusCode.OK, common.StatusCode);
        var yaml = await contract.Content.ReadAsStringAsync(Ct);
        Assert.StartsWith("openapi: 3.1", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Errors_AreProblemDetailsWithRequestId()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/rota-que-nao-existe");
        request.Headers.Add(RequestIdMiddleware.Header, "smoke-req");

        using var response = await _http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("smoke-req", problem.GetProperty("request_id").GetString());
    }
}
