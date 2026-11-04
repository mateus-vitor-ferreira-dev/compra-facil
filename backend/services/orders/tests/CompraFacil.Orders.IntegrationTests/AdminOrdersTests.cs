using System.Net;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Orders.Domain;
using CompraFacil.Orders.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Orders.IntegrationTests;

/// <summary>
/// A operação dos pedidos: a lista de todos (com o contato, sem CPF nem endereço), o detalhe e o resumo de
/// vendas. Cada teste cria os pedidos dele num ano só seu, para não somar os dos outros testes.
/// </summary>
public sealed class AdminOrdersTests(OrdersFixture fx) : IClassFixture<OrdersFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ShippingAddress s_address = new("37203775", "Rua A", "30", null, "Centro", "Lavras", "MG");

    private static OrderItem Item(Guid product, string name, int quantity, long cents) =>
        new(product, "SKU-" + name, name, quantity, new Money(cents, "BRL"), null);

    /// <summary>Um pedido criado em <paramref name="at"/> e levado até <paramref name="end"/>.</summary>
    private static Order Make(DateTimeOffset at, string end, string email, PaymentMethod method = PaymentMethod.Pix, params OrderItem[] items)
    {
        var o = Order.Place(Guid.NewGuid(), Guid.NewGuid(), email, items.Length == 0 ? [Item(Guid.NewGuid(), "Mouse", 1, 10000)] : items,
            Guid.NewGuid().ToString(), "h", at, new Recipient("Bia Lima", "52998224725", "35999990000"), s_address,
            new OrderShipping("economico", "Econômico", new Money(1500, "BRL"), 3, 7, DateOnly.FromDateTime(at.AddDays(7).DateTime)),
            method, method == PaymentMethod.Card ? 3 : 1);
        if (end == "pending")
        {
            return o;
        }
        if (end == "out_of_stock")
        {
            o.Cancel(CancellationReason.OutOfStock, at);
            return o;
        }
        o.MarkStockReserved(at);
        if (end == "payment_failed")
        {
            o.Cancel(CancellationReason.PaymentFailed, at, Guid.NewGuid(), "card_declined");
            return o;
        }
        o.Confirm(Guid.NewGuid(), at);
        if (end == "supplier_failed")
        {
            o.FailAtSupplier(at);
            return o;
        }
        if (end is "delivered" or "returned")
        {
            o.MarkSentToSupplier(at);
            o.Ship(new Tracking("BR1", null, null), at);
            o.Deliver(at);
            if (end == "returned")
            {
                o.RequestReturn(at);
            }
        }
        return o;
    }

    private async Task SaveAsync(params Order[] orders)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        db.Orders.AddRange(orders);
        await db.SaveChangesAsync(Ct);
    }

    private HttpClient Admin()
    {
        var http = fx.Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", fx.Identity.AdminToken());
        return http;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }

    [Fact]
    public async Task So_a_equipe_ve_a_lista_e_o_resumo()
    {
        using var customer = fx.ClientAs();
        foreach (var path in new[] { "/v1/admin/orders", "/v1/admin/sales-summary?from=2030-01-01T00:00:00Z&to=2030-02-01T00:00:00Z" })
        {
            using var forbidden = await customer.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using var anonymous = await fx.Factory.CreateClient().GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
    }

    [Fact]
    public async Task Lista_traz_o_contato_sem_CPF_nem_endereco_e_filtra()
    {
        var day = new DateTimeOffset(2031, 3, 10, 15, 0, 0, TimeSpan.Zero);
        var email = $"lista-{Guid.NewGuid():N}@exemplo.com";
        var older = Make(day, "delivered", email);
        var newer = Make(day.AddHours(1), "pending", email);
        await SaveAsync(older, newer, Make(day, "delivered", "outro@exemplo.com"));
        using var http = Admin();

        var page = await JsonAsync(await http.GetAsync($"/v1/admin/orders?q={email}", Ct));
        var data = page.GetProperty("data");
        Assert.Equal([newer.Id, older.Id], data.EnumerateArray().Select(o => o.GetProperty("id").GetGuid()));
        var first = data[0];
        ContractAssert.AssertSchema("orders.v1.yaml", "/components/schemas/AdminOrderSummary", first.GetRawText());
        Assert.Equal(("Bia Lima", email, "35999990000"), (first.GetProperty("customer").GetProperty("name").GetString(),
            first.GetProperty("customer").GetProperty("email").GetString(), first.GetProperty("customer").GetProperty("phone").GetString()));
        Assert.Equal("Lavras", first.GetProperty("destination").GetProperty("city").GetString());
        var raw = first.GetRawText();
        Assert.DoesNotContain("52998224725", raw, StringComparison.Ordinal); // o CPF não sai
        Assert.DoesNotContain("Rua A", raw, StringComparison.Ordinal); // nem a rua

        // filtro por situação, pelo começo do número e pelo nome; "%" digitado não vira curinga
        var delivered = await JsonAsync(await http.GetAsync($"/v1/admin/orders?q={email}&status=delivered", Ct));
        Assert.Equal(older.Id, Assert.Single(delivered.GetProperty("data").EnumerateArray()).GetProperty("id").GetGuid());
        var byNumber = await JsonAsync(await http.GetAsync($"/v1/admin/orders?q={newer.Id.ToString()[..8].ToUpperInvariant()}", Ct));
        Assert.Contains(byNumber.GetProperty("data").EnumerateArray(), o => o.GetProperty("id").GetGuid() == newer.Id);
        var wildcard = await JsonAsync(await http.GetAsync("/v1/admin/orders?q=%25", Ct));
        Assert.Empty(wildcard.GetProperty("data").EnumerateArray());

        // paginação por cursor
        var one = await JsonAsync(await http.GetAsync($"/v1/admin/orders?q={email}&limit=1", Ct));
        var cursor = one.GetProperty("page").GetProperty("next_cursor").GetString();
        var two = await JsonAsync(await http.GetAsync($"/v1/admin/orders?q={email}&limit=1&cursor={cursor}", Ct));
        Assert.Equal(older.Id, two.GetProperty("data")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Detalhe_traz_itens_e_etapas_sem_CPF()
    {
        var at = new DateTimeOffset(2031, 4, 2, 12, 0, 0, TimeSpan.Zero);
        var order = Make(at, "delivered", "detalhe@exemplo.com", PaymentMethod.Card, Item(Guid.NewGuid(), "Fone", 2, 5000));
        await SaveAsync(order);
        using var http = Admin();

        var json = await JsonAsync(await http.GetAsync($"/v1/admin/orders/{order.Id}", Ct));
        ContractAssert.AssertSchema("orders.v1.yaml", "/components/schemas/AdminOrderDetail", json.GetRawText());
        Assert.Equal(2, json.GetProperty("items_count").GetInt32());
        Assert.Equal(("card", 3), (json.GetProperty("payment").GetProperty("method").GetString(), json.GetProperty("payment").GetProperty("installments").GetInt32()));
        Assert.Equal("BR1", json.GetProperty("tracking").GetProperty("number").GetString());
        Assert.NotEqual(JsonValueKind.Null, json.GetProperty("delivered_at").ValueKind);
        Assert.DoesNotContain("52998224725", json.GetRawText(), StringComparison.Ordinal);

        using var missing = await http.GetAsync($"/v1/admin/orders/{Guid.NewGuid()}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Resumo_soma_pagos_estornos_dias_meios_e_os_mais_vendidos()
    {
        // 2032: um ano só deste teste. Dia 10/1 às 01:00 UTC ainda é 9/1 em Brasília
        var (fone, mouse) = (Guid.NewGuid(), Guid.NewGuid());
        var d1 = new DateTimeOffset(2032, 1, 10, 1, 0, 0, TimeSpan.Zero); // 9/1 em Brasília
        var d2 = new DateTimeOffset(2032, 1, 11, 15, 0, 0, TimeSpan.Zero);
        await SaveAsync(
            Make(d1, "delivered", "r1@exemplo.com", PaymentMethod.Pix, Item(fone, "Fone", 2, 5000)), // 100 + 15 de frete
            Make(d2, "pending", "r2@exemplo.com"), // 3 sem pagar, de jeitos diferentes
            Make(d2, "out_of_stock", "r3@exemplo.com"),
            Make(d2, "payment_failed", "r4@exemplo.com", PaymentMethod.Card),
            Make(d2, "returned", "r5@exemplo.com", PaymentMethod.Card, Item(mouse, "Mouse", 1, 30000)), // 300 + 15, estornado
            Make(d2, "supplier_failed", "r6@exemplo.com", PaymentMethod.Pix, Item(mouse, "Mouse", 1, 30000)), // idem
            Make(d2, "confirmed", "r7@exemplo.com", PaymentMethod.Card, Item(mouse, "Mouse", 1, 8000))); // 80 + 15
        using var http = Admin();

        var s = await JsonAsync(await http.GetAsync("/v1/admin/sales-summary?from=2032-01-09T03:00:00Z&to=2032-01-12T03:00:00Z", Ct));
        ContractAssert.AssertSchema("orders.v1.yaml", "/components/schemas/SalesSummary", s.GetRawText());
        long Cents(JsonElement e, string name) => e.GetProperty(name).GetProperty("amount_cents").GetInt64();
        var orders = s.GetProperty("orders");
        Assert.Equal((7, 4, 2, 3), (orders.GetProperty("placed").GetInt32(), orders.GetProperty("paid").GetInt32(),
            orders.GetProperty("refunded").GetInt32(), orders.GetProperty("not_paid").GetInt32()));
        Assert.Equal(11500 + 31500 + 31500 + 9500, Cents(s, "gross_sales"));
        Assert.Equal(63000, Cents(s, "refunds"));
        Assert.Equal(11500 + 9500, Cents(s, "net_sales"));
        Assert.Equal(21000, Cents(s, "average_ticket")); // 84000 / 4
        Assert.Equal(4 * 1500, Cents(s, "shipping_charged"));

        // três dias de Brasília (9, 10 e 11), também o que não teve venda
        var daily = s.GetProperty("daily").EnumerateArray().ToList();
        Assert.Equal(["2032-01-09", "2032-01-10", "2032-01-11"], daily.Select(d => d.GetProperty("date").GetString()));
        Assert.Equal([1, 0, 3], daily.Select(d => d.GetProperty("paid_orders").GetInt32()));
        Assert.Equal(63000, Cents(daily[2], "refunds"));

        var methods = s.GetProperty("by_payment_method").EnumerateArray()
            .ToDictionary(m => m.GetProperty("method").GetString()!, m => (m.GetProperty("paid_orders").GetInt32(), Cents(m, "gross_sales")));
        Assert.Equal((2, 31500 + 9500L), methods["card"]);
        Assert.Equal((2, 11500 + 31500L), methods["pix"]);

        // os mais vendidos contam só o que ficou vendido: os dois mouses estornados saem
        var top = s.GetProperty("top_products").EnumerateArray().ToList();
        Assert.Equal([("Fone", 2, 10000L), ("Mouse", 1, 8000L)],
            top.Select(p => (p.GetProperty("name").GetString(), p.GetProperty("quantity").GetInt32(), Cents(p, "revenue"))));
    }

    [Fact]
    public async Task Resumo_recusa_periodo_invertido_ou_maior_que_um_ano()
    {
        using var http = Admin();
        foreach (var query in new[]
        {
            "from=2030-02-01T00:00:00Z&to=2030-01-01T00:00:00Z",
            "from=2030-01-01T00:00:00Z&to=2031-03-01T00:00:00Z",
            "from=2030-01-01T00:00:00Z",
        })
        {
            using var response = await http.GetAsync($"/v1/admin/sales-summary?{query}", Ct);
            Assert.True(response.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest, $"{query}: {response.StatusCode}");
        }
    }
}
