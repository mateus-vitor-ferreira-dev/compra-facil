using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Inventory.Application.Stock;

namespace CompraFacil.Inventory.IntegrationTests;

/// <summary>API do inventory (consulta e ajuste), com as respostas validadas no contrato OpenAPI.</summary>
public sealed class StockApiTests(InventoryFixture fx) : IClassFixture<InventoryFixture>
{
    private readonly HttpClient _http = fx.Factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpResponseMessage> AdjustAsync(Guid product, object body, string? key = null, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/admin/stock/{product}/adjustments")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? fx.AdminToken());
        return await _http.SendAsync(request, Ct);
    }

    [Fact]
    public async Task GetStock_FollowsTheContract_Or404()
    {
        var product = await fx.ProductWithStockAsync(onHand: 9);

        using var found = await _http.GetAsync(new Uri($"/v1/stock/{product}", UriKind.Relative), Ct);
        using var missing = await _http.GetAsync(new Uri($"/v1/stock/{Guid.NewGuid()}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.NotNull(found.Headers.ETag);
        var json = await found.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("inventory.v1.yaml", "/components/schemas/StockLevel", json);
        Assert.Equal(9, JsonDocument.Parse(json).RootElement.GetProperty("available").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ListStock_ShowsUnknownProductsZeroed()
    {
        var product = await fx.ProductWithStockAsync(onHand: 3);
        var unknown = Guid.NewGuid();

        using var response = await _http.GetAsync(new Uri($"/v1/stock?product_ids={product},{unknown}", UriKind.Relative), Ct);

        var json = await response.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("inventory.v1.yaml", "/paths/~1v1~1stock/get/responses/200/content/application~1json/schema", json);
        var levels = JsonDocument.Parse(json).RootElement.GetProperty("data").EnumerateArray()
            .ToDictionary(l => l.GetProperty("product_id").GetGuid(), l => l.GetProperty("on_hand").GetInt32());
        Assert.Equal(3, levels[product]);
        Assert.Equal(0, levels[unknown]);
    }

    [Theory]
    [InlineData("product_ids=nao-e-uuid")]
    [InlineData("product_ids=")]
    public async Task ListStock_InvalidIds_Is422(string query)
    {
        using var response = await _http.GetAsync(new Uri($"/v1/stock?{query}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("\"field\":\"product_ids\"", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReservation_FollowsTheContract()
    {
        var product = await fx.ProductWithStockAsync(onHand: 5);
        var order = Guid.NewGuid();
        await fx.HandleAsync<OrderPlacedData, ReserveStock>(new OrderPlacedData(order, [new OrderLine(product, 2)]));

        using var response = await _http.GetAsync(new Uri($"/v1/reservations/{order}", UriKind.Relative), Ct);
        using var missing = await _http.GetAsync(new Uri($"/v1/reservations/{Guid.NewGuid()}", UriKind.Relative), Ct);

        var json = await response.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("inventory.v1.yaml", "/components/schemas/Reservation", json);
        Assert.Equal("reserved", JsonDocument.Parse(json).RootElement.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Adjust_AddsStock_AndCreatesUnknownProducts()
    {
        var product = Guid.NewGuid(); // ainda sem registro

        using var response = await AdjustAsync(product, new { delta = 50, reason = "restock", note = "NF 123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("inventory.v1.yaml", "/components/schemas/StockLevel", json);
        Assert.Equal(50, JsonDocument.Parse(json).RootElement.GetProperty("on_hand").GetInt32());
    }

    [Fact]
    public async Task Adjust_BelowReserved_Is409()
    {
        var product = await fx.ProductWithStockAsync(onHand: 5);
        await fx.HandleAsync<OrderPlacedData, ReserveStock>(new OrderPlacedData(Guid.NewGuid(), [new OrderLine(product, 4)]));

        using var response = await AdjustAsync(product, new { delta = -2, reason = "damage" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("reservados", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adjust_IsIdempotent_AndRejectsAReusedKeyWithAnotherBody()
    {
        var product = await fx.ProductWithStockAsync(onHand: 5);

        using var first = await AdjustAsync(product, new { delta = 10, reason = "restock" }, key: "ajuste-idem-1");
        using var again = await AdjustAsync(product, new { delta = 10, reason = "restock" }, key: "ajuste-idem-1");
        using var other = await AdjustAsync(product, new { delta = 99, reason = "restock" }, key: "ajuste-idem-1");

        Assert.Equal(await first.Content.ReadAsStringAsync(Ct), await again.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);
        using var stock = await _http.GetAsync(new Uri($"/v1/stock/{product}", UriKind.Relative), Ct);
        Assert.Equal(15, (await stock.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("on_hand").GetInt32());
    }

    [Fact]
    public async Task Adjust_InvalidBody_Is422()
    {
        using var response = await AdjustAsync(Guid.NewGuid(), new { delta = 0, reason = "presente" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("common/v1/common.yaml", "/components/schemas/ValidationProblem", json);
        Assert.Contains("\"field\":\"delta\"", json, StringComparison.Ordinal);
        Assert.Contains("\"field\":\"reason\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adjust_RequiresAnAdminToken()
    {
        var product = Guid.NewGuid();
        using var anonymous = new HttpRequestMessage(HttpMethod.Post, $"/v1/admin/stock/{product}/adjustments")
        {
            Content = JsonContent.Create(new { delta = 1, reason = "restock" }),
        };
        anonymous.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        using var noToken = await _http.SendAsync(anonymous, Ct);
        using var viewer = await AdjustAsync(product, new { delta = 1, reason = "restock" }, token: fx.AdminToken(["customer"]));

        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewer.StatusCode);
    }
}
