using System.Net;
using System.Text;
using System.Text.Json;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;
using CompraFacil.Fulfillment.Infrastructure.Supplier;
using Microsoft.Extensions.Options;

namespace CompraFacil.Fulfillment.IntegrationTests;

/// <summary>O cliente do CJ contra respostas HTTP simuladas: token, aceite, recusas e erros passageiros.</summary>
public sealed class CjSupplierGatewayTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Responde pelo caminho; guarda os pedidos recebidos.</summary>
    private sealed class Stub(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Path, string Body, string? Token)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.RequestUri!.AbsolutePath, body,
                request.Headers.TryGetValues("CJ-Access-Token", out var t) ? t.Single() : null));
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static object Token(string token) => new
    {
        code = 200,
        result = true,
        message = "Success",
        data = new { accessToken = token, accessTokenExpiryDate = DateTimeOffset.UtcNow.AddDays(15) },
    };

    private static (CjSupplierGateway Gateway, Stub Stub) Gateway(Func<HttpRequestMessage, string, HttpResponseMessage> createOrder, bool scenario = false)
    {
        var stub = new Stub((req, body) => req.RequestUri!.AbsolutePath.EndsWith("getAccessToken", StringComparison.Ordinal)
            ? Json(Token("tok-" + Guid.NewGuid().ToString("N")[..6]))
            : createOrder(req, body));
        var options = Options.Create(new SupplierOptions { BaseUrl = new Uri("http://sim/api2.0/v1/"), ScenarioFromEmail = scenario });
        var http = new HttpClient(stub) { BaseAddress = options.Value.BaseUrl };
        return (new CjSupplierGateway(http, new CjTokenCache(options, TimeProvider.System), options), stub);
    }

    private static SupplierOrderRequest Request(string email = "ana@exemplo.com") =>
        new(Guid.NewGuid(), email, [new SupplierOrderItem("MOU-000001", "Mouse", 2)]);

    [Fact]
    public async Task Aceite_traz_o_id_e_o_token_e_reaproveitado()
    {
        var (gateway, stub) = Gateway((_, _) => Json(new { code = 200, result = true, message = "Success", data = new { orderId = "CJ1234567890", orderStatus = "CREATED" } }));

        var first = await gateway.PlaceOrderAsync(Request(), Ct);
        await gateway.PlaceOrderAsync(Request(), Ct);

        Assert.Equal(new SupplierPlacement.Accepted("CJ1234567890"), first);
        Assert.Single(stub.Requests, r => r.Path.EndsWith("getAccessToken", StringComparison.Ordinal));
        var order = stub.Requests.First(r => r.Path.EndsWith("createOrderV2", StringComparison.Ordinal));
        Assert.NotNull(order.Token);
        using var json = JsonDocument.Parse(order.Body);
        Assert.Equal("MOU-000001", json.RootElement.GetProperty("products")[0].GetProperty("vid").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("products")[0].GetProperty("quantity").GetInt32());
    }

    [Theory]
    [InlineData(1603001, SupplierFailureReasons.OutOfStock)]
    [InlineData(1603002, SupplierFailureReasons.Rejected)]
    [InlineData(1600100, SupplierFailureReasons.Rejected)]
    public async Task Recusas_de_negocio(int code, string reason)
    {
        var (gateway, _) = Gateway((_, _) => Json(new { code, result = false, message = "não", data = (object?)null }));

        var result = await gateway.PlaceOrderAsync(Request(), Ct);

        Assert.Equal(reason, Assert.IsType<SupplierPlacement.Rejected>(result).Reason);
    }

    [Fact]
    public async Task Erro_5xx_e_codigo_desconhecido_sao_passageiros()
    {
        var (down, _) = Gateway((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var (odd, _) = Gateway((_, _) => Json(new { code = 1699999, result = false, message = "?", data = (object?)null }));

        await Assert.ThrowsAsync<SupplierUnavailableException>(() => down.PlaceOrderAsync(Request(), Ct));
        await Assert.ThrowsAsync<SupplierUnavailableException>(() => odd.PlaceOrderAsync(Request(), Ct));
    }

    [Fact]
    public async Task Token_recusado_e_renovado_uma_vez()
    {
        var calls = 0;
        var (gateway, stub) = Gateway((_, _) => ++calls == 1
            ? Json(new { code = 1600001, result = false, message = "Invalid token", data = (object?)null }, HttpStatusCode.Unauthorized)
            : Json(new { code = 200, result = true, message = "Success", data = new { orderId = "CJ1" } }));

        Assert.IsType<SupplierPlacement.Accepted>(await gateway.PlaceOrderAsync(Request(), Ct));
        Assert.Equal(2, stub.Requests.Count(r => r.Path.EndsWith("getAccessToken", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Cenario_do_simulado_vem_do_email()
    {
        var (gateway, stub) = Gateway((_, _) => Json(new { code = 200, result = true, message = "Success", data = new { orderId = "CJ1" } }), scenario: true);

        await gateway.PlaceOrderAsync(Request("ana+recusar-depois@exemplo.com"), Ct);

        using var json = JsonDocument.Parse(stub.Requests.Last().Body);
        Assert.Equal("[recusar-depois]", json.RootElement.GetProperty("remark").GetString());
    }

    [Fact]
    public async Task Feed_le_todas_as_paginas_e_pula_item_sem_preco()
    {
        object Page(int from, int count, int total) => new
        {
            code = 200,
            result = true,
            message = "Success",
            data = new
            {
                total,
                list = Enumerable.Range(from, count).Select(n => new
                {
                    vid = $"V-{n}",
                    productSku = $"CJ{n}",
                    productNameEn = $"Item {n}",
                    sellPrice = n == 3 ? "abc" : "12.40",
                    categoryName = "Mouse",
                    productImage = "",
                    productWeight = 80,
                }),
            },
        };
        var (gateway, stub) = Gateway((req, _) => req.RequestUri!.Query.Contains("pageNum=1", StringComparison.Ordinal)
            ? Json(Page(1, 200, 201))
            : Json(Page(201, 1, 201)));

        var feed = await gateway.ListProductsAsync(Ct);

        Assert.Equal(200, feed.Count); // 201 itens, um sem preço válido
        Assert.DoesNotContain(feed, i => i.Vid == "V-3");
        Assert.Equal(new SupplierFeedItem("V-1", "CJ1", "Item 1", "Mouse", 1240, "USD", null, 80), feed[0]);
        Assert.Equal(2, stub.Requests.Count(r => r.Path.EndsWith("product/list", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Feed_com_erro_e_passageiro()
    {
        var (gateway, _) = Gateway((_, _) => Json(new { code = 1600100, result = false, message = "Invalid params" }));

        await Assert.ThrowsAsync<SupplierUnavailableException>(() => gateway.ListProductsAsync(Ct));
    }
}
