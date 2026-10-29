using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Fulfillment.Domain;
using CompraFacil.Fulfillment.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Fulfillment.IntegrationTests;

public sealed class OverdueFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    public TestIdentity Identity { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _postgres.ConnectionString)
            .UseSetting("Kafka:Enabled", "false")
            .UseSetting("Supplier:DispatcherEnabled", "false")
            .UseSetting("Supplier:CatalogSyncEnabled", "false")
            .UseSetting("Supplier:Deadlines:ShipWithin", "3.00:00:00")
            .UseSetting("Supplier:Deadlines:DeliverWithin", "15.00:00:00")
            .UseSetting("Auth:Jwks", Identity.Jwks));
        using var _ = Factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
        Identity.Dispose();
    }
}

/// <summary>Os pedidos parados além do prazo do fornecedor, para o painel.</summary>
public sealed class OverdueOrdersTests(OverdueFixture fx) : IClassFixture<OverdueFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset s_now = DateTimeOffset.UtcNow;

    private static SupplierOrder Paid(TimeSpan ago, DateOnly? promisedBy = null) =>
        SupplierOrder.Create(Guid.NewGuid(), "ana@exemplo.com", [new SupplierOrderItem("MOU-1", "Mouse", 1)], "cj", s_now - ago, promisedBy: promisedBy);

    private static DateOnly Today(int plusDays) => DateOnly.FromDateTime(s_now.UtcDateTime.AddHours(-3)).AddDays(plusDays);

    private async Task SaveAsync(params SupplierOrder[] orders)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>();
        db.SupplierOrders.AddRange(orders);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<HttpResponseMessage> GetAsync(string? token, string query = "")
    {
        using var client = fx.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/admin/supplier-orders/overdue" + query);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task ListsTheOrdersPastTheSupplierDeadlines_MostLateFirst()
    {
        var stuckLong = Paid(TimeSpan.FromDays(10)); // na fila há 10 dias: venceu há 7
        var accepted = Paid(TimeSpan.FromDays(4)); // aceito e não despachado: venceu há 1
        accepted.MarkPlaced("CJ1", s_now - TimeSpan.FromDays(4));
        var lost = Paid(TimeSpan.FromDays(25)); // enviado há 20 dias: venceu há 5
        lost.MarkShipped(new Tracking("BR1", "Correios", null), s_now - TimeSpan.FromDays(20));
        var onTime = Paid(TimeSpan.FromDays(1));
        var travelling = Paid(TimeSpan.FromDays(10));
        travelling.MarkShipped(new Tracking("BR2", null, null), s_now - TimeSpan.FromDays(5));
        var delivered = Paid(TimeSpan.FromDays(40));
        delivered.MarkDelivered(s_now - TimeSpan.FromDays(30));
        var failed = Paid(TimeSpan.FromDays(40));
        failed.Fail(SupplierFailureReasons.Rejected, null, s_now - TimeSpan.FromDays(40));
        // com a data prometida ao cliente (#222), vale ela: venceu anteontem, mesmo enviado há pouco...
        var promisedLate = Paid(TimeSpan.FromDays(6), promisedBy: Today(-2));
        promisedLate.MarkShipped(new Tracking("BR3", null, null), s_now - TimeSpan.FromDays(1));
        // ...e enviado há 20 dias, mas prometido para a semana que vem, ainda está no prazo
        var promisedLater = Paid(TimeSpan.FromDays(25), promisedBy: Today(7));
        promisedLater.MarkShipped(new Tracking("BR4", null, null), s_now - TimeSpan.FromDays(20));
        await SaveAsync(stuckLong, accepted, lost, onTime, travelling, delivered, failed, promisedLate, promisedLater);

        using var response = await GetAsync(fx.Identity.AdminToken());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("fulfillment.v1.yaml",
            "/paths/~1v1~1admin~1supplier-orders~1overdue/get/responses/200/content/application~1json/schema", json);
        var data = JsonDocument.Parse(json).RootElement.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal([stuckLong.Id, lost.Id, promisedLate.Id, accepted.Id], data.Select(o => o.GetProperty("order_id").GetGuid()));
        Assert.Equal(["shipping", "delivery", "delivery", "shipping"], data.Select(o => o.GetProperty("stage").GetString()));
        Assert.Equal("BR1", data[1].GetProperty("tracking").GetProperty("number").GetString());
    }

    [Fact]
    public async Task RequiresTheAdminToken_AndAValidLimit()
    {
        using var anonymous = await GetAsync(null);
        using var customer = await GetAsync(fx.Identity.CustomerToken());
        using var badLimit = await GetAsync(fx.Identity.AdminToken(), "?limit=0");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badLimit.StatusCode);
    }
}
