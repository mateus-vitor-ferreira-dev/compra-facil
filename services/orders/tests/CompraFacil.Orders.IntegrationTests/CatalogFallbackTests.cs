using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Orders.Infrastructure;
using CompraFacil.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Orders.IntegrationTests;

/// <summary>
/// Catalog fora (#81): o pedido sai pela cópia local do catálogo, com as mesmas regras do
/// ValidateItems; o circuit breaker para de chamar o catalog. Fixture própria: o estado do circuito
/// não vaza para os outros testes.
/// </summary>
public sealed class CatalogFallbackTests(OrdersFixture fx) : IClassFixture<OrdersFixture>, IDisposable
{
    private readonly HttpClient _http = fx.ClientAs(); // um cliente: o dono dos pedidos do teste

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => fx.Catalog.Down = false;

    private async Task<Guid> SnapshotProductAsync(long cents, string status = "active", bool deleted = false, params (int Discount, int StartedHoursAgo, int EndsInHours)[] campaigns)
    {
        var id = Guid.NewGuid();
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        db.ProductSnapshots.Add(new ProductSnapshotRecord
        {
            ProductId = id,
            Sku = "SNAP-" + cents,
            Name = "Produto da cópia",
            CategoryId = Guid.NewGuid(),
            PriceCents = cents,
            Currency = "BRL",
            Status = status,
            Version = 1,
            Deleted = deleted,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var now = DateTimeOffset.UtcNow;
        foreach (var (discount, started, ends) in campaigns)
        {
            db.CampaignSnapshots.Add(new CampaignSnapshotRecord
            {
                CampaignId = Guid.NewGuid(),
                Slug = $"camp-{Guid.NewGuid():N}",
                Name = $"{discount}%",
                DiscountPercent = discount,
                StartsAt = now.AddHours(-started),
                EndsAt = now.AddHours(ends),
                ProductIds = [id],
                Version = 1,
            });
        }
        await db.SaveChangesAsync(Ct);
        return id;
    }

    private async Task<HttpResponseMessage> PlaceAsync(Guid product, long? expected = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/orders")
        {
            Content = JsonContent.Create(new
            {
                items = new[] { new { product_id = product, quantity = 2, expected_unit_price_cents = expected } },
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await _http.SendAsync(request, Ct);
    }

    private async Task<Guid?> CampaignOfAsync(int discount, Guid product)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().CampaignSnapshots
            .Where(c => c.DiscountPercent == discount && c.ProductIds.Contains(product))
            .Select(c => (Guid?)c.CampaignId).SingleOrDefaultAsync(Ct);
    }

    [Fact]
    public async Task CatalogDown_PricesFromTheSnapshot_WithTheBestActiveCampaign()
    {
        // vigentes de 10% e 25%; a de 50% já acabou: vale a de 25%, arredondando a favor do cliente
        var product = await SnapshotProductAsync(9999, campaigns: [(10, 1, 1), (25, 1, 1), (50, 3, -1)]);
        fx.Catalog.Down = true;

        using var response = await PlaceAsync(product, expected: 7499);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var item = order.GetProperty("items")[0];
        Assert.Equal(7499, item.GetProperty("unit_price").GetProperty("amount_cents").GetInt64());
        Assert.Equal(await CampaignOfAsync(25, product), item.GetProperty("campaign_id").GetGuid());
        Assert.Equal("Produto da cópia", item.GetProperty("name").GetString());
        Assert.Equal(7499 * 2, order.GetProperty("total").GetProperty("amount_cents").GetInt64());

        // e a SAGA começa igual: OrderPlaced no outbox
        var id = order.GetProperty("id").GetGuid();
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var outbox = await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Set<OutboxMessage>()
            .SingleAsync(m => m.MessageKey == id.ToString(), Ct);
        Assert.Equal("OrderPlaced", outbox.EventType);
    }

    [Theory]
    [InlineData("inactive", false, null, "não está à venda")]
    [InlineData("active", true, null, "não existe")]
    [InlineData("active", false, 1000L, "o preço mudou")]
    public async Task CatalogDown_SnapshotAppliesTheCatalogRules(string status, bool deleted, long? expected, string detail)
    {
        var product = await SnapshotProductAsync(2000, status, deleted);
        fx.Catalog.Down = true;

        using var response = await PlaceAsync(product, expected);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Contains(detail, problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogDown_ProductNotInTheSnapshot_Is503()
    {
        fx.Catalog.Down = true;

        using var response = await PlaceAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task CircuitOpens_StopsCallingTheCatalog_AndClosesWhenItIsBack()
    {
        var product = await SnapshotProductAsync(1500);
        fx.Catalog.Down = true;
        var before = fx.Catalog.Calls;

        // cada pedido tenta 3 vezes; com 10 chamadas na janela e metade falhando, o circuito abre (as
        // falhas dos outros testes da classe também contam, então pode abrir antes)
        for (var i = 0; i < 6; i++)
        {
            using var response = await PlaceAsync(product);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); // todos pela cópia local
        }
        var whileOpen = fx.Catalog.Calls;
        Assert.InRange(whileOpen - before, 1, 6 * 3 - 1); // sem o circuito seriam 18
        using (var response = await PlaceAsync(product))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        Assert.Equal(whileOpen, fx.Catalog.Calls); // aberto: nem tenta

        // catalog de volta e passado o BreakDuration (0,5 s nos testes): a chamada de teste passa e fecha
        fx.Catalog.Down = false;
        var catalogProduct = fx.Catalog.Add(new CatalogProduct("SKU-VOLTOU", "Do catalog", 1234));
        await Task.Delay(TimeSpan.FromMilliseconds(700), Ct);
        using (var response = await PlaceAsync(catalogProduct))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var order = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("Do catalog", order.GetProperty("items")[0].GetProperty("name").GetString());
        }
        Assert.Equal(whileOpen + 1, fx.Catalog.Calls);
    }
}
