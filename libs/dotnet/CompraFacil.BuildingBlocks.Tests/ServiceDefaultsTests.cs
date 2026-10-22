using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Web;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CompraFacil.BuildingBlocks.Tests;

public sealed class ServiceDefaultsTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _http = null!;
    private static bool s_ready = true;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddServiceDefaults(new ServiceInfo("test", "test.v1.yaml"));
        builder.Services.AddControllers().AddApplicationPart(typeof(TestController).Assembly);
        builder.Services.AddHealthChecks().AddCheck("dependencia", () =>
            s_ready ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("fora"), tags: [ServiceDefaults.ReadyTag]);

        _app = builder.Build();
        _app.UseServiceDefaults();
        _app.MapControllers();
        _app.MapGet("/boom", (Func<IResult>)(() => throw new InvalidOperationException("detalhe interno")));
        _app.MapGet("/invalid", (Func<IResult>)(() => throw new ValidationException(
        [
            new ValidationFailure("Items[0].ProductId", "produto obrigatório"),
            new ValidationFailure("CustomerEmail", "e-mail inválido"),
        ])));
        await _app.StartAsync();
        _http = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> ProblemOf(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    [Fact]
    public async Task RequestId_IsReusedAndEchoed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(RequestIdMiddleware.Header, "req-123");

        using var response = await _http.SendAsync(request, Ct);

        Assert.Equal("req-123", response.Headers.GetValues(RequestIdMiddleware.Header).Single());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(129)]
    public async Task RequestId_IsGeneratedWhenMissingOrTooLong(object? header)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        var value = header switch { int n => new string('x', n), string s => s, _ => null };
        if (value is not null)
        {
            request.Headers.TryAddWithoutValidation(RequestIdMiddleware.Header, value);
        }

        using var response = await _http.SendAsync(request, Ct);

        var id = response.Headers.GetValues(RequestIdMiddleware.Header).Single();
        Assert.True(Guid.TryParse(id, out _), $"esperava um GUID novo, veio '{id}'");
    }

    [Fact]
    public async Task UnknownRoute_IsProblemDetailsWithRequestId()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/nada");
        request.Headers.Add(RequestIdMiddleware.Header, "req-404");

        using var response = await _http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ProblemOf(response);
        Assert.Equal("about:blank", problem.GetProperty("type").GetString());
        Assert.Equal("Recurso não encontrado", problem.GetProperty("title").GetString());
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal("/v1/nada", problem.GetProperty("instance").GetString());
        Assert.Equal("req-404", problem.GetProperty("request_id").GetString());
        // trace_id (W3C) fica de propósito, em snake_case como o resto do JSON
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", problem.GetProperty("trace_id").GetString());
        Assert.False(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task UnhandledException_Is500WithoutInternals()
    {
        using var response = await _http.GetAsync(new Uri("/boom", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await ProblemOf(response);
        Assert.Equal(500, problem.GetProperty("status").GetInt32());
        Assert.Equal("Erro interno", problem.GetProperty("title").GetString());
        Assert.True(problem.TryGetProperty("request_id", out _));
        Assert.DoesNotContain("detalhe interno", problem.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationException_Is422WithFieldsInJsonPath()
    {
        using var response = await _http.GetAsync(new Uri("/invalid", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await ProblemOf(response);
        var errors = problem.GetProperty("errors").EnumerateArray()
            .Select(e => (e.GetProperty("field").GetString(), e.GetProperty("message").GetString()))
            .ToList();
        Assert.Equal([("items[0].product_id", "produto obrigatório"), ("customer_email", "e-mail inválido")], errors);
    }

    [Fact]
    public async Task MalformedJson_Is400Problem()
    {
        using var body = new StringContent("{\"quantity\": ", Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync(new Uri("/test", UriKind.Relative), body, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ProblemOf(response);
        Assert.Equal("Requisição malformada", problem.GetProperty("title").GetString());
        Assert.Equal("about:blank", problem.GetProperty("type").GetString());
        Assert.True(problem.TryGetProperty("request_id", out _));
    }

    [Fact]
    public async Task Json_IsSnakeCase()
    {
        using var response = await _http.PostAsJsonAsync("/test", new { product_id = Guid.Empty, quantity = 2 }, Ct);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("\"line_quantity\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"in_stock\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_IgnoresDependencies_ReadyDoesNot()
    {
        s_ready = false;
        try
        {
            using var health = await _http.GetAsync(new Uri("/health", UriKind.Relative), Ct);
            using var ready = await _http.GetAsync(new Uri("/ready", UriKind.Relative), Ct);

            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        }
        finally
        {
            s_ready = true;
        }
    }

    [Fact]
    public async Task Docs_ServeContractAndSwaggerUi()
    {
        var page = await _http.GetStringAsync(new Uri("/docs", UriKind.Relative), Ct);
        using var contract = await _http.GetAsync(new Uri("/openapi/test.v1.yaml", UriKind.Relative), Ct);
        using var common = await _http.GetAsync(new Uri("/openapi/common/v1/common.yaml", UriKind.Relative), Ct);

        Assert.Contains("url: \"openapi/test.v1.yaml\"", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, contract.StatusCode);
        Assert.Equal("application/yaml", contract.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, common.StatusCode);
    }

    [Fact]
    public async Task Docs_FailFastWhenContractIsMissing()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddServiceDefaults(new ServiceInfo("x", "nao-existe.v1.yaml"));
        await using var app = builder.Build();

        var error = Assert.Throws<InvalidOperationException>(() => app.UseServiceDefaults());
        Assert.Contains("nao-existe.v1.yaml", error.Message, StringComparison.Ordinal);
    }
}

[ApiController]
[Route("test")]
public sealed class TestController : ControllerBase
{
    public sealed record Input(Guid ProductId, int Quantity);

    public enum StockStatus { InStock, OutOfStock }

    public sealed record Output(int LineQuantity, StockStatus Status);

    [HttpPost]
#pragma warning disable CA1822 // action de controller não pode ser estática
    public Output Post(Input input) => new(input.Quantity, StockStatus.InStock);
#pragma warning restore CA1822
}
