using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Orders.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Orders.IntegrationTests;

/// <summary>API de pedidos de ponta a ponta: HTTP → caso de uso → gRPC (catalog falso) → Postgres + outbox.</summary>
public sealed class OrdersApiTests(OrdersFixture fx) : IClassFixture<OrdersFixture>
{
    private readonly HttpClient _http = fx.Factory.CreateClient(); // sem token: cada chamada leva o do cliente

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Corpo do pedido (só itens) e o cliente cujo token vai junto.</summary>
    private sealed record PlaceBody(Guid Customer, object Json);

    private static object Pix => new { method = "pix" };

    private static PlaceBody Body(Guid customerId, params object[] items) => new(customerId, new { items, payment = Pix });

    /// <summary>Quem recebe e onde (#221): o PlaceAsync os põe no corpo que não os traz.</summary>
    private static object Recipient => new { name = "Ana Souza", cpf = "529.982.247-25", phone = "(11) 98765-4321" };

    private static object Address => new
    {
        zip_code = "01310-100",
        street = "Avenida Paulista",
        number = "1000",
        complement = "Apto 12",
        district = "Bela Vista",
        city = "São Paulo",
        state = "sp",
    };

    private async Task<HttpResponseMessage> PlaceAsync(
        PlaceBody body, string? key = null, string? requestId = null, string? token = null, bool withDelivery = true)
    {
        var json = JsonSerializer.SerializeToNode(body.Json)!.AsObject();
        if (withDelivery)
        {
            json.TryAdd("recipient", JsonSerializer.SerializeToNode(Recipient));
            json.TryAdd("shipping_address", JsonSerializer.SerializeToNode(Address));
            json.TryAdd("shipping", JsonSerializer.SerializeToNode(new { service = "economico" }));
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/orders") { Content = JsonContent.Create(json) };
        request.Headers.Authorization = new("Bearer", token ?? fx.Identity.CustomerToken(body.Customer));
        if (key != "<sem>")
        {
            request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        }
        if (requestId is not null)
        {
            request.Headers.Add(RequestIdMiddleware.Header, requestId);
        }
        return await _http.SendAsync(request, Ct);
    }

    private async Task<List<OutboxMessage>> OutboxForAsync(Guid orderId)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await db.Set<OutboxMessage>().Where(m => m.MessageKey == orderId.ToString()).ToListAsync(Ct);
    }

    private async Task<int> OrdersOfAsync(Guid customerId)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.CountAsync(o => o.CustomerId == customerId, Ct);
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    [Fact]
    public async Task Place_WithCard_SendsTheTokenToThePayments_WithoutKeepingIt()
    {
        var item = fx.Catalog.Add(new CatalogProduct("PAY-1", "Mouse", 9990));
        var customer = Guid.NewGuid();
        using var response = await PlaceAsync(new PlaceBody(customer, new
        {
            items = new[] { new { product_id = item, quantity = 1 } },
            payment = new { method = "card", card_token = "tok_123", card_brand = "visa", installments = 3 },
        }));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("orders.v1.yaml", "/components/schemas/Order", json);
        Assert.DoesNotContain("tok_123", json, StringComparison.Ordinal);
        var order = JsonDocument.Parse(json).RootElement;
        Assert.Equal(("card", 3), (order.GetProperty("payment").GetProperty("method").GetString(), order.GetProperty("payment").GetProperty("installments").GetInt32()));

        var placed = EventEnvelope.TryParse(Assert.Single(await OutboxForAsync(order.GetProperty("id").GetGuid())).Payload)!;
        var payment = placed.Data.GetProperty("payment");
        Assert.Equal(("tok_123", "visa"), (payment.GetProperty("card_token").GetString(), payment.GetProperty("card_brand").GetString()));
        Assert.Equal("ana@exemplo.com", placed.Data.GetProperty("customer_email").GetString());
    }

    [Fact]
    public async Task Place_KeepsWhoReceivesAndWhere_Normalized_AndSendsThemWithTheConfirmation()
    {
        var item = fx.Catalog.Add(new CatalogProduct("ADDR-1", "Mouse", 9990));
        using var response = await PlaceAsync(Body(Guid.NewGuid(), new { product_id = item, quantity = 1 }));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("orders.v1.yaml", "/components/schemas/Order", json);
        var order = JsonDocument.Parse(json).RootElement;
        var recipient = order.GetProperty("recipient");
        Assert.Equal(("Ana Souza", "52998224725", "11987654321"),
            (recipient.GetProperty("name").GetString(), recipient.GetProperty("cpf").GetString(), recipient.GetProperty("phone").GetString()));
        var address = order.GetProperty("shipping_address");
        Assert.Equal(("01310100", "SP", "Apto 12"),
            (address.GetProperty("zip_code").GetString(), address.GetProperty("state").GetString(), address.GetProperty("complement").GetString()));
    }

    [Fact]
    public async Task Place_WithoutWhoReceivesOrWithAnInvalidCpf_Is422_WithTheFields()
    {
        var item = fx.Catalog.Add(new CatalogProduct("ADDR-2", "Mouse", 9990));
        using var missing = await PlaceAsync(Body(Guid.NewGuid(), new { product_id = item, quantity = 1 }), withDelivery: false);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        Assert.Superset(new HashSet<string?> { "recipient", "shipping_address" }, await FieldsOf(missing));

        using var invalid = await PlaceAsync(new PlaceBody(Guid.NewGuid(), new
        {
            items = new[] { new { product_id = item, quantity = 1 } },
            payment = Pix,
            recipient = new { name = "Ana", cpf = "529.982.247-24", phone = "123" },
            shipping_address = new { zip_code = "0131", street = "", number = "1", district = "Centro", city = "São Paulo", state = "XX" },
        }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        Assert.Superset(new HashSet<string?>
        {
            "recipient.cpf", "recipient.phone", "shipping_address.zip_code", "shipping_address.street", "shipping_address.state",
        }, await FieldsOf(invalid));
    }

    [Fact]
    public async Task Place_RequotesTheShipping_AddsItToTheTotal_AndPromisesTheDate()
    {
        var item = fx.Catalog.Add(new CatalogProduct("SHIP-1", "Mouse", 9990));
        using var response = await PlaceAsync(Body(Guid.NewGuid(), new { product_id = item, quantity = 2 }));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("orders.v1.yaml", "/components/schemas/Order", json);
        var order = JsonDocument.Parse(json).RootElement;
        var shipping = order.GetProperty("shipping");
        Assert.Equal(("economico", 2430L, 21), (shipping.GetProperty("service").GetString(),
            shipping.GetProperty("price").GetProperty("amount_cents").GetInt64(), shipping.GetProperty("max_days").GetInt32()));
        Assert.Equal(19980L, order.GetProperty("items_total").GetProperty("amount_cents").GetInt64());
        Assert.Equal(19980L + 2430, order.GetProperty("total").GetProperty("amount_cents").GetInt64());
        var promised = DateOnly.Parse(shipping.GetProperty("promised_by").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(promised > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21)), "21 dias úteis passam de 21 dias corridos");
        // o fulfillment cotou o CEP e os SKUs do pedido
        Assert.Contains(fx.Shipping.Calls, c => c.Zip == "01310100" && c.Items.Contains(("SHIP-1", 2)));
        // o payments cobra o total com o frete
        var placed = EventEnvelope.TryParse(Assert.Single(await OutboxForAsync(order.GetProperty("id").GetGuid())).Payload)!;
        Assert.Equal(19980L + 2430, placed.Data.GetProperty("total").GetProperty("amount_cents").GetInt64());
    }

    [Fact]
    public async Task Place_WithAShippingTheZipDoesNotHave_Is422_AndWithTheQuoteDown_Is503()
    {
        var item = fx.Catalog.Add(new CatalogProduct("SHIP-2", "Mouse", 9990));
        using var unknown = await PlaceAsync(new PlaceBody(Guid.NewGuid(), new
        {
            items = new[] { new { product_id = item, quantity = 1 } },
            payment = Pix,
            shipping = new { service = "drone" },
        }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        Assert.Contains("shipping.service", await FieldsOf(unknown));

        var options = fx.Shipping.Options;
        fx.Shipping.Options = null;
        try
        {
            using var down = await PlaceAsync(Body(Guid.NewGuid(), new { product_id = item, quantity = 1 }));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
            Assert.NotNull(down.Headers.RetryAfter);
        }
        finally
        {
            fx.Shipping.Options = options;
        }
    }

    private static async Task<HashSet<string?>> FieldsOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString()).ToHashSet();

    [Theory]
    [InlineData("card", null, 1)]       // cartão sem token
    [InlineData("card", "tok", 13)]     // até 12x
    [InlineData("pix", null, 2)]        // PIX é à vista
    [InlineData("boleto", null, 1)]     // meio que não existe
    public async Task Place_RejectsAnInvalidPayment(string method, string? token, int installments)
    {
        var item = fx.Catalog.Add(new CatalogProduct("PAY-2", "Mouse", 9990));
        using var response = await PlaceAsync(new PlaceBody(Guid.NewGuid(), new
        {
            items = new[] { new { product_id = item, quantity = 1 } },
            payment = new { method, card_token = token, card_brand = "visa", installments },
        }));

        Assert.True(response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest, response.StatusCode.ToString());
    }

    [Fact]
    public async Task Place_Returns202Pending_FreezesPrices_AndPublishesOrderPlaced()
    {
        var campaign = Guid.NewGuid();
        var fone = fx.Catalog.Add(new CatalogProduct("AUD-1", "Fone Sonix", 19990, CampaignId: campaign));
        var cabo = fx.Catalog.Add(new CatalogProduct("CAB-1", "Cabo USB-C", 2990));
        var customer = Guid.NewGuid();

        using var response = await PlaceAsync(Body(customer,
            new { product_id = fone, quantity = 2, expected_unit_price_cents = 19990 },
            new { product_id = cabo, quantity = 1 }), requestId: "req-place-1");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("orders.v1.yaml", "/components/schemas/Order", json);
        var order = JsonDocument.Parse(json).RootElement;
        var id = order.GetProperty("id").GetGuid();
        Assert.Equal($"/v1/orders/{id}", response.Headers.Location?.ToString());
        Assert.Equal("pending", order.GetProperty("status").GetString());
        Assert.Equal(19990 * 2 + 2990 + 2430, order.GetProperty("total").GetProperty("amount_cents").GetInt64());
        var first = order.GetProperty("items")[0];
        Assert.Equal("Fone Sonix", first.GetProperty("name").GetString());
        Assert.Equal(campaign, first.GetProperty("campaign_id").GetGuid());
        Assert.Equal(39980, first.GetProperty("line_total").GetProperty("amount_cents").GetInt64());

        // OrderPlaced no outbox, no formato do contrato AsyncAPI, no fluxo da requisição
        var outbox = Assert.Single(await OutboxForAsync(id));
        Assert.Equal("orders.order.v1", outbox.Topic);
        ContractAssert.AssertEvent("orders.v1.yaml", "OrderPlaced", outbox.Payload);
        var envelope = EventEnvelope.TryParse(outbox.Payload)!;
        Assert.Equal("req-place-1", envelope.CorrelationId);
        Assert.Equal(id, envelope.Data.GetProperty("order_id").GetGuid());
    }

    [Fact]
    public async Task Place_SameKeyAndBody_ReturnsTheSameOrder()
    {
        var product = fx.Catalog.Add(new CatalogProduct("SKU-IDEM", "Produto", 1000));
        var customer = Guid.NewGuid();
        var body = Body(customer, new { product_id = product, quantity = 1 });

        using var first = await PlaceAsync(body, key: "chave-idempotente-1");
        using var second = await PlaceAsync(body, key: "chave-idempotente-1");

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var id = (await JsonOf(first)).GetProperty("id").GetGuid();
        Assert.Equal(id, (await JsonOf(second)).GetProperty("id").GetGuid());
        Assert.Equal(1, await OrdersOfAsync(customer));
        Assert.Single(await OutboxForAsync(id));
    }

    [Fact]
    public async Task Place_ConcurrentRequestsWithSameKey_CreateOneOrder()
    {
        var product = fx.Catalog.Add(new CatalogProduct("SKU-RACE", "Produto", 1000));
        var customer = Guid.NewGuid();
        var body = Body(customer, new { product_id = product, quantity = 1 });

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PlaceAsync(body, key: "chave-corrida-1")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        var ids = await Task.WhenAll(responses.Select(async r => (await JsonOf(r)).GetProperty("id").GetGuid()));
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await OrdersOfAsync(customer));
        foreach (var r in responses)
        {
            r.Dispose();
        }
    }

    [Fact]
    public async Task Place_SameKeyDifferentBody_Is422()
    {
        var product = fx.Catalog.Add(new CatalogProduct("SKU-DIFF", "Produto", 1000));
        var customer = Guid.NewGuid();

        using var first = await PlaceAsync(Body(customer, new { product_id = product, quantity = 1 }), key: "chave-reusada-1");
        using var second = await PlaceAsync(Body(customer, new { product_id = product, quantity = 3 }), key: "chave-reusada-1");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        var problem = await second.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("common/v1/common.yaml", "/components/schemas/ValidationProblem", problem);
        Assert.Contains("\"field\":\"idempotency_key\"", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Place_WithoutIdempotencyKey_Is400()
    {
        using var response = await PlaceAsync(Body(Guid.NewGuid(), new { product_id = Guid.NewGuid(), quantity = 1 }), key: "<sem>");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        ContractAssert.AssertSchema("common/v1/common.yaml", "/components/schemas/Problem", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Place_InvalidBody_Is422WithJsonPaths_AndDoesNotCallTheCatalog()
    {
        var calls = fx.Catalog.Calls;
        var repeated = Guid.NewGuid();

        using var response = await PlaceAsync(new PlaceBody(Guid.NewGuid(), new
        {
            items = new object[]
            {
                new { product_id = repeated, quantity = 0 },
                new { product_id = repeated, quantity = 1, expected_unit_price_cents = 0 },
            },
        }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("common/v1/common.yaml", "/components/schemas/ValidationProblem", problem);
        var fields = JsonDocument.Parse(problem).RootElement.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString()).ToHashSet();
        // sem o meio de pagamento também (#220): o checkout sempre escolhe PIX ou cartão
        Assert.Superset(new HashSet<string?> { "items", "items[0].quantity", "items[1].expected_unit_price_cents", "payment" }, fields);
        Assert.Equal(calls, fx.Catalog.Calls);
    }

    [Fact]
    public async Task Place_ItemsRejectedByTheCatalog_Is409_AndStoresNothing()
    {
        var inactive = fx.Catalog.Add(new CatalogProduct("SKU-OFF", "Fora de linha", 1000, Active: false));
        var repriced = fx.Catalog.Add(new CatalogProduct("SKU-NEW", "Remarcado", 1500));
        var customer = Guid.NewGuid();

        using var response = await PlaceAsync(Body(customer,
            new { product_id = inactive, quantity = 1 },
            new { product_id = repriced, quantity = 1, expected_unit_price_cents = 1000 },
            new { product_id = Guid.NewGuid(), quantity = 1 }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await JsonOf(response);
        var detail = problem.GetProperty("detail").GetString()!;
        Assert.Contains("SKU-OFF", detail, StringComparison.Ordinal);
        Assert.Contains("1500", detail, StringComparison.Ordinal);
        Assert.Contains("não existe", detail, StringComparison.Ordinal);
        Assert.Equal(0, await OrdersOfAsync(customer));
    }

    [Fact]
    public async Task Place_CatalogDown_Is503WithRetryAfter_AndStoresNothing()
    {
        var product = fx.Catalog.Add(new CatalogProduct("SKU-DOWN", "Produto", 1000));
        var customer = Guid.NewGuid();
        fx.Catalog.Down = true;
        try
        {
            using var response = await PlaceAsync(Body(customer, new { product_id = product, quantity = 1 }));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.NotNull(response.Headers.RetryAfter?.Delta);
            ContractAssert.AssertSchema("common/v1/common.yaml", "/components/schemas/Problem", await response.Content.ReadAsStringAsync(Ct));
            Assert.Equal(0, await OrdersOfAsync(customer));
        }
        finally
        {
            fx.Catalog.Down = false;
        }
    }

    [Fact]
    public async Task Get_ReturnsTheOrder_Or404()
    {
        var product = fx.Catalog.Add(new CatalogProduct("SKU-GET", "Produto", 1000));
        var owner = Guid.NewGuid();
        using var placed = await PlaceAsync(Body(owner, new { product_id = product, quantity = 3 }));
        var placedJson = await JsonOf(placed);
        var id = placedJson.GetProperty("id").GetGuid();

        using var found = await GetAsync($"/v1/orders/{id}", fx.Identity.CustomerToken(owner));
        using var missing = await GetAsync($"/v1/orders/{Guid.NewGuid()}", fx.Identity.CustomerToken(owner));

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var json = await found.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("orders.v1.yaml", "/components/schemas/Order", json);
        Assert.Equal(3000 + 2430, JsonDocument.Parse(json).RootElement.GetProperty("total").GetProperty("amount_cents").GetInt64());
        // datas na precisão do banco: a resposta da criação bate com a leitura
        Assert.Equal(placedJson.GetProperty("created_at").GetString(),
            JsonDocument.Parse(json).RootElement.GetProperty("created_at").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task List_PaginatesNewestFirst_AndFilters()
    {
        var product = fx.Catalog.Add(new CatalogProduct("SKU-LIST", "Produto", 1000));
        var customer = Guid.NewGuid();
        var created = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            using var r = await PlaceAsync(Body(customer, new { product_id = product, quantity = i + 1 }));
            created.Add((await JsonOf(r)).GetProperty("id").GetGuid());
        }
        using (var other = await PlaceAsync(Body(Guid.NewGuid(), new { product_id = product, quantity = 1 })))
        {
            Assert.Equal(HttpStatusCode.Accepted, other.StatusCode); // de outro cliente: não aparece
        }

        var token = fx.Identity.CustomerToken(customer);
        using var page1 = await GetAsync("/v1/me/orders?limit=2", token);
        var json1 = await page1.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("orders.v1.yaml", "/paths/~1v1~1me~1orders/get/responses/200/content/application~1json/schema", json1);
        var body1 = JsonDocument.Parse(json1).RootElement;
        Assert.True(body1.GetProperty("page").GetProperty("has_more").GetBoolean());
        var cursor = body1.GetProperty("page").GetProperty("next_cursor").GetString();

        using var page2 = await GetAsync($"/v1/me/orders?limit=2&cursor={cursor}", token);
        var body2 = await JsonOf(page2);
        Assert.False(body2.GetProperty("page").GetProperty("has_more").GetBoolean());

        var listed = body1.GetProperty("data").EnumerateArray().Concat(body2.GetProperty("data").EnumerateArray())
            .Select(o => o.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(Enumerable.Reverse(created), listed);

        using var pending = await GetAsync("/v1/me/orders?status=pending", token);
        using var confirmed = await GetAsync("/v1/me/orders?status=confirmed", token);
        Assert.Equal(3, (await JsonOf(pending)).GetProperty("data").GetArrayLength());
        Assert.Equal(0, (await JsonOf(confirmed)).GetProperty("data").GetArrayLength());
    }

    [Theory]
    [InlineData("limit=101", "limit")]
    [InlineData("cursor=lixo", "cursor")]
    public async Task List_InvalidQuery_Is422(string query, string field)
    {
        using var response = await GetAsync($"/v1/me/orders?{query}", fx.Identity.CustomerToken());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains($"\"field\":\"{field}\"", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    // ---------- quem pode o quê (o cliente vem do token) ----------

    private async Task<HttpResponseMessage> GetAsync(string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }
        return await _http.SendAsync(request, Ct);
    }

    [Fact]
    public async Task Place_TakesTheCustomerAndEmailFromTheToken_IgnoringTheBody()
    {
        var product = fx.Catalog.Add(new CatalogProduct("SKU-TOKEN", "Produto", 1000));
        var customer = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();

        // cliente antigo ainda mandando customer_id/customer_email: valem os do token
        using var response = await PlaceAsync(
            new PlaceBody(customer, new { customer_id = someoneElse, customer_email = "outro@exemplo.com", items = new[] { new { product_id = product, quantity = 1 } }, payment = Pix }),
            token: fx.Identity.CustomerToken(customer, "bia@exemplo.com"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var order = await JsonOf(response);
        var id = order.GetProperty("id").GetGuid();
        Assert.Equal(customer, order.GetProperty("customer_id").GetGuid());
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Orders.SingleAsync(o => o.Id == id, Ct);
            Assert.Equal("bia@exemplo.com", saved.CustomerEmail); // destino dos e-mails da SAGA
        }
        Assert.Equal(0, await OrdersOfAsync(someoneElse));
    }

    [Fact]
    public async Task Place_WithoutToken_Is401_AndAsOperator_Is403()
    {
        var body = Body(Guid.NewGuid(), new { product_id = Guid.NewGuid(), quantity = 1 });

        using var anonymous = new HttpRequestMessage(HttpMethod.Post, "/v1/orders") { Content = JsonContent.Create(body.Json) };
        anonymous.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var noToken = await _http.SendAsync(anonymous, Ct);
        using var asOperator = await PlaceAsync(body, token: fx.Identity.AdminToken());

        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asOperator.StatusCode);
    }

    [Fact]
    public async Task Get_OtherCustomersOrder_Is404_ButTheOperatorSeesIt()
    {
        var product = fx.Catalog.Add(new CatalogProduct("SKU-DONO", "Produto", 1000));
        using var placed = await PlaceAsync(Body(Guid.NewGuid(), new { product_id = product, quantity = 1 }));
        var id = (await JsonOf(placed)).GetProperty("id").GetGuid();

        using var stranger = await GetAsync($"/v1/orders/{id}", fx.Identity.CustomerToken());
        using var operatorView = await GetAsync($"/v1/orders/{id}", fx.Identity.AdminToken());
        using var anonymous = await GetAsync($"/v1/orders/{id}", null);

        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.OK, operatorView.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task MyOrders_WithoutToken_Is401_AndAsOperator_Is403()
    {
        using var anonymous = await GetAsync("/v1/me/orders", null);
        using var asOperator = await GetAsync("/v1/me/orders", fx.Identity.AdminToken());

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asOperator.StatusCode);
    }
}
