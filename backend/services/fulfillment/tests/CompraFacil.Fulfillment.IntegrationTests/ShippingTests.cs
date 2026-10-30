using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Fulfillment.Application.SupplierOrders;

namespace CompraFacil.Fulfillment.IntegrationTests;

/// <summary>A cotação de frete por CEP (#222): o fornecedor cota em dólar, a loja mostra em reais com o prazo dela.</summary>
public sealed class ShippingTests(FulfillmentFixture fx) : IClassFixture<FulfillmentFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpResponseMessage> QuoteAsync(object body)
    {
        using var client = fx.Factory.CreateClient(); // sem token: a cotação é pública
        return await client.PostAsJsonAsync("/v1/shipping/quote", body, Ct);
    }

    [Fact]
    public async Task Cota_em_reais_com_o_processamento_da_loja_do_mais_barato_ao_mais_caro()
    {
        using var response = await QuoteAsync(new { zip_code = "01310-100", items = new[] { new { sku = "SIM-V-001", quantity = 2 } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(Ct);
        ContractAssert.AssertSchema("fulfillment.v1.yaml", "/components/schemas/ShippingQuote", json);
        var quote = JsonDocument.Parse(json).RootElement;
        Assert.Equal("01310100", quote.GetProperty("zip_code").GetString());
        var options = quote.GetProperty("options").EnumerateArray().ToList();
        // o serviço que a loja não conhece fica de fora; US$ 4,50 a R$ 5,40 = R$ 24,30; prazo + 1 dia útil
        Assert.Equal(2, options.Count);
        Assert.Equal(("economico", "Econômico", 2430L, 13, 21), (options[0].GetProperty("id").GetString(), options[0].GetProperty("name").GetString(),
            options[0].GetProperty("price_cents").GetInt64(), options[0].GetProperty("min_days").GetInt32(), options[0].GetProperty("max_days").GetInt32()));
        Assert.Equal("expresso", options[1].GetProperty("id").GetString());
        var call = fx.Supplier.FreightCalls[^1];
        Assert.Equal(("01310100", "SIM-V-001", 2), (call.Zip, call.Items[0].Sku, call.Items[0].Quantity));
    }

    [Fact]
    public async Task CEP_ou_itens_invalidos_e_422_e_fornecedor_fora_e_503()
    {
        using var invalid = await QuoteAsync(new { zip_code = "0131", items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

        var freight = fx.Supplier.Freight;
        fx.Supplier.Freight = null;
        try
        {
            using var down = await QuoteAsync(new { zip_code = "01310100", items = new[] { new { sku = "SIM-V-001", quantity = 1 } } });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
            Assert.NotNull(down.Headers.RetryAfter);
        }
        finally
        {
            fx.Supplier.Freight = freight;
        }
    }
}
