using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
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
    private readonly string _connectionString;
    private HttpClient _http = null!;

    protected ServiceSmokeTests(PostgresFixture postgres)
    {
        ArgumentNullException.ThrowIfNull(postgres);
        _connectionString = postgres.ConnectionString;
        _factory = new WebApplicationFactory<TProgram>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", postgres.ConnectionString)
            .UseSetting("Kafka:Enabled", "false")); // o smoke não precisa de broker
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
    public async Task Migrations_CreateTheMessagingTables()
    {
        using var _ = _factory.CreateClient(); // sobe o host: MigrateOnStart aplica as migrations

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(Ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('outbox', 'inbox') ORDER BY 1",
            conn);
        var tables = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                tables.Add(reader.GetString(0));
            }
        }
        Assert.Equal(["inbox", "outbox"], tables);
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
        using var deadLetters = await _http.GetAsync(new Uri("/openapi/common/v1/dead-letters.yaml", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, contract.StatusCode);
        Assert.Equal(HttpStatusCode.OK, common.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deadLetters.StatusCode);
        var yaml = await contract.Content.ReadAsStringAsync(Ct);
        Assert.StartsWith("openapi: 3.1", yaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET", "/v1/admin/dead-letters")]
    [InlineData("POST", "/v1/admin/dead-letters/replay")]
    public async Task DeadLetters_RequireTheAdminToken(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await _http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
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
