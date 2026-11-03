using CompraFacil.Fulfillment.Application.Shipping;
using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.UnitTests;

/// <summary>
/// O frete grátis pela margem (#323): a opção sai de graça só quando o preço da loja, menos o custo no fornecedor
/// e a taxa do pior caso, paga o frete inteiro.
/// </summary>
public sealed class FreeShippingTests
{
    private static readonly Guid s_cheap = Guid.NewGuid();
    private static readonly Guid s_pricey = Guid.NewGuid();
    private static readonly Guid s_ownStock = Guid.NewGuid();

    // custo no fornecedor em dólar: 2,00 e 10,00 (câmbio 5,00 → R$ 10,00 e R$ 50,00); o de estoque próprio não tem
    private static readonly SupplierProduct[] s_supplied =
    [
        SupplierProduct.Create("cj", new SupplierFeedItem("V-CHEAP", "V-CHEAP", "Capa", "acessorios", 200, "USD", null, null), DateTimeOffset.UnixEpoch),
        SupplierProduct.Create("cj", new SupplierFeedItem("V-PRICEY", "V-PRICEY", "Fone", "audio", 1000, "USD", null, null), DateTimeOffset.UnixEpoch),
    ];

    // o econômico custa US$ 4,00 (R$ 20,00) e o expresso US$ 10,00 (R$ 50,00)
    private sealed class Supplier : ISupplierGateway
    {
        public bool Down { get; set; }

        public string Name => "cj";

        public Task<IReadOnlyList<SupplierFreightOption>> QuoteFreightAsync(string zipCode, IReadOnlyList<SupplierOrderItem> items, CancellationToken ct) =>
            Down
                ? throw new SupplierUnavailableException("fora do ar")
                : Task.FromResult<IReadOnlyList<SupplierFreightOption>>(
                    [new("CJ Express", 1000, 3, 5), new("CJPacket Ordinary", 400, 8, 12)]);

        public Task<SupplierPlacement> PlaceOrderAsync(SupplierOrderRequest request, CancellationToken ct) => throw new NotSupportedException();

        public Task<SupplierReturnOpening> OpenReturnAsync(Guid orderNumber, string customerEmail, IReadOnlyList<SupplierOrderItem> items, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<bool> CancelOrderAsync(string supplierOrderId, CancellationToken ct) => throw new NotSupportedException();

        public Task CheckAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<SupplierFeedItem>> ListProductsAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, int>> GetStockAsync(IReadOnlyCollection<string> vids, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Products : ISupplierProductRepository
    {
        public Task<List<SupplierProduct>> GetManyAsync(string supplier, IReadOnlyCollection<string> vids, CancellationToken ct) =>
            Task.FromResult(s_supplied.Where(p => vids.Contains(p.Vid)).ToList());

        public Task<bool> RunExclusiveAsync(Func<Task> work, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<SupplierProduct>> ListAsync(string supplier, CancellationToken ct) => throw new NotSupportedException();

        public Task<(int InFeed, DateTimeOffset? LastStockReadAt)> FeedSummaryAsync(string supplier, CancellationToken ct) => throw new NotSupportedException();

        public void Add(SupplierProduct product) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Prices(IReadOnlyDictionary<Guid, StorePrice>? prices) : IStorePrices
    {
        public Task<IReadOnlyDictionary<Guid, StorePrice>?> GetAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct) => Task.FromResult(prices);
    }

    // preço da loja: a capa a R$ 50,00, o fone a R$ 200,00, e o de estoque próprio a R$ 300,00
    private static readonly Dictionary<Guid, StorePrice> s_storePrices = new()
    {
        [s_cheap] = new("V-CHEAP", 5_000),
        [s_pricey] = new("V-PRICEY", 20_000),
        [s_ownStock] = new("PROPRIO-1", 30_000),
    };

    private static ShippingQuotes Quotes(IReadOnlyDictionary<Guid, StorePrice>? prices = null, bool enabled = true, Supplier? supplier = null) =>
        new(supplier ?? new Supplier(), new Products(), new Prices(prices ?? s_storePrices),
            new ShippingSettings { UsdToBrl = 5.00m, WorstPaymentFeePercent = 10m, FreeShipping = enabled });

    private static async Task<Dictionary<string, long>> PriceAsync(ShippingQuotes quotes, params ShippingItem[] items) =>
        (await quotes.QuoteAsync("01310100", items, CancellationToken.None)).ToDictionary(o => o.Id, o => o.PriceCents);

    [Fact]
    public async Task Margem_que_paga_o_economico_e_nao_o_expresso_da_frete_gratis_so_no_economico()
    {
        // fone: R$ 200,00 - R$ 50,00 de custo - R$ 20,00 de taxa = R$ 130,00 ≥ R$ 20,00 (econômico) e ≥ R$ 50,00 (expresso)
        // capa: R$ 50,00 - R$ 10,00 - R$ 5,00 = R$ 35,00 ≥ R$ 20,00 (econômico), < R$ 50,00 (expresso)
        var prices = await PriceAsync(Quotes(), new ShippingItem("V-CHEAP", 1, s_cheap));

        Assert.Equal(0, prices["economico"]);
        Assert.Equal(5_000, prices["expresso"]);
    }

    [Fact]
    public async Task Margem_que_nao_paga_o_frete_inteiro_nao_da_frete_gratis()
    {
        // a capa a R$ 30,00: R$ 30,00 - R$ 10,00 - R$ 3,00 = R$ 17,00 < R$ 20,00
        var cheap = new Dictionary<Guid, StorePrice>(s_storePrices) { [s_cheap] = new("V-CHEAP", 3_000) };

        var prices = await PriceAsync(Quotes(cheap), new ShippingItem("V-CHEAP", 1, s_cheap));

        Assert.Equal(2_000, prices["economico"]);
        Assert.Equal(5_000, prices["expresso"]);
    }

    [Fact]
    public async Task Opcao_gratis_mostra_o_preco_normal_e_vem_primeiro()
    {
        var options = await Quotes().QuoteAsync("01310100", [new ShippingItem("V-PRICEY", 1, s_pricey)], CancellationToken.None);

        Assert.All(options, o => Assert.True(o.Free));
        Assert.Equal(["economico", "expresso"], options.Select(o => o.Id));
        Assert.Equal(2_000, options[0].CostCents);
    }

    [Fact]
    public async Task O_preco_vem_do_catalogo_e_nao_do_navegador()
    {
        // o navegador manda outro SKU: vale o do catálogo para o produto, e a conta usa o preço da loja
        var prices = await PriceAsync(Quotes(), new ShippingItem("SKU-FORJADO", 1, s_cheap));

        Assert.Equal(0, prices["economico"]);
    }

    [Theory]
    [InlineData("sem o product_id")]
    [InlineData("produto sem custo")]
    [InlineData("catalog fora do ar")]
    [InlineData("desligado")]
    public async Task Sem_como_garantir_que_nao_ha_prejuizo_nao_ha_frete_gratis(string caso)
    {
        var prices = caso switch
        {
            "sem o product_id" => await PriceAsync(Quotes(), new ShippingItem("V-PRICEY", 1)),
            "produto sem custo" => await PriceAsync(Quotes(), new ShippingItem("V-PRICEY", 1, s_pricey), new ShippingItem("PROPRIO-1", 1, s_ownStock)),
            "catalog fora do ar" => await PriceAsync(new ShippingQuotes(new Supplier(), new Products(), new Prices(null),
                new ShippingSettings { UsdToBrl = 5.00m }), new ShippingItem("V-PRICEY", 1, s_pricey)),
            _ => await PriceAsync(Quotes(enabled: false), new ShippingItem("V-PRICEY", 1, s_pricey)),
        };

        Assert.Equal(2_000, prices["economico"]);
        Assert.Equal(5_000, prices["expresso"]);
    }

    [Fact]
    public async Task Custo_do_frete_para_a_margem_e_nulo_com_o_fornecedor_fora()
    {
        var supplier = new Supplier();
        Assert.Equal(2_000, await Quotes(supplier: supplier).CostAsync("01310100", [new ShippingItem("V-CHEAP", 1)], "economico", CancellationToken.None));

        supplier.Down = true;
        Assert.Null(await Quotes(supplier: supplier).CostAsync("01310100", [new ShippingItem("V-CHEAP", 1)], "economico", CancellationToken.None));
    }
}
