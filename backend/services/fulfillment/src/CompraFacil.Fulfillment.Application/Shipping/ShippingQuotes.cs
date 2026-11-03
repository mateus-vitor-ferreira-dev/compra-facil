using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application.Shipping;

/// <summary>
/// Um item a cotar: o SKU da loja (o vid do fornecedor) e a quantidade. Com o produto da loja, o frete grátis é
/// avaliado com o preço do catálogo (#323); sem ele, só a cotação.
/// </summary>
public sealed record ShippingItem(string Sku, int Quantity, Guid? ProductId = null);

/// <summary>
/// Uma opção de envio como o cliente vê: o serviço, o preço cobrado em reais e o prazo em dias úteis, já com o
/// processamento da loja somado ao do fornecedor. <see cref="CostCents"/> é o que o fornecedor cobra da loja, e
/// não vai ao cliente; no frete grátis, <see cref="PriceCents"/> é zero.
/// </summary>
public sealed record ShippingOption(string Id, string Name, long PriceCents, int MinDays, int MaxDays, long CostCents)
{
    public bool Free => PriceCents == 0 && CostCents > 0;
}

/// <summary>
/// Seção Shipping: o câmbio do frete (o do catálogo), o processamento da loja, os nomes dos serviços e a regra do
/// frete grátis.
/// </summary>
public sealed class ShippingSettings
{
    public const string Section = "Shipping";

    /// <summary>Reais por dólar, o mesmo câmbio do catálogo (CATALOG_SUPPLIER_USD_BRL).</summary>
    public decimal UsdToBrl { get; set; } = 5.40m;

    /// <summary>Dias úteis da loja antes de o pedido sair (a nota e o pedido ao fornecedor).</summary>
    public int ProcessingDays { get; set; } = 1;

    /// <summary>O nome que o cliente vê de cada serviço do fornecedor; o que não está aqui não é oferecido.</summary>
    public Dictionary<string, ShippingService> Services { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CJPacket Ordinary"] = new() { Id = "economico", Name = "Econômico" },
        ["CJ Express"] = new() { Id = "expresso", Name = "Expresso" },
    };

    /// <summary>Liga o frete grátis pela margem (#323).</summary>
    public bool FreeShipping { get; set; } = true;

    /// <summary>
    /// A maior taxa de pagamento que o pedido pode ter, em porcentagem: o frete é cotado antes da escolha do
    /// pagamento, então a conta usa o pior caso. Padrão: o cartão em 6x, o máximo do checkout (Payments:Fees).
    /// </summary>
    public decimal WorstPaymentFeePercent { get; set; } = 12.9m;
}

public sealed class ShippingService
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>O preço que a loja cobra por um produto agora (com a campanha) e o SKU dele, pelo catálogo.</summary>
public sealed record StorePrice(string Sku, long UnitPriceCents);

/// <summary>Os preços da loja, pelo catálogo (gRPC). Nulo quando o catálogo não respondeu: aí não há frete grátis.</summary>
public interface IStorePrices
{
    Task<IReadOnlyDictionary<Guid, StorePrice>?> GetAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct);
}

/// <summary>
/// A cotação de frete e prazo por CEP (#222, Decreto 7.962: antes de pagar). Pergunta ao fornecedor, converte
/// o preço para reais (arredondado para cima, no centavo) e soma o processamento da loja ao prazo. As opções
/// saem da mais barata para a mais cara.
///
/// Frete grátis (#323): a loja nunca sai no prejuízo. Uma opção sai de graça quando a margem dos produtos paga o
/// frete inteiro: o preço da loja (do catálogo, nunca do navegador), menos o custo no fornecedor e a taxa de
/// pagamento do pior caso, cobre o custo daquele frete. Produto sem custo conhecido (que não veio do
/// fornecedor) ou catálogo fora do ar: sem frete grátis.
/// </summary>
public sealed class ShippingQuotes(ISupplierGateway supplier, ISupplierProductRepository products, IStorePrices prices, ShippingSettings settings)
{
    public const int MaxItems = 50;

    /// <exception cref="SupplierUnavailableException">O fornecedor não respondeu.</exception>
    public async Task<IReadOnlyList<ShippingOption>> QuoteAsync(string zipCode, IReadOnlyList<ShippingItem> items, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(items);
        // com o produto da loja, o SKU vem do catálogo: a cotação e a margem usam o mesmo produto
        var store = settings.FreeShipping && items.All(i => i.ProductId is not null)
            ? await prices.GetAsync([.. items.Select(i => i.ProductId!.Value).Distinct()], ct)
            : null;
        var quoted = store is null
            ? items
            : [.. items.Select(i => store.TryGetValue(i.ProductId!.Value, out var p) ? i with { Sku = p.Sku } : i)];

        var options = await SupplierOptionsAsync(zipCode, quoted, ct);
        var margin = store is null ? null : await MarginAsync(quoted, store, ct);
        return [.. options
            .Select(o => margin is { } m && m >= o.CostCents ? o with { PriceCents = 0 } : o)
            .OrderBy(o => o.PriceCents)
            .ThenBy(o => o.CostCents)];
    }

    /// <summary>
    /// O custo do frete para a loja no serviço escolhido, para a margem do pedido (#323). Nulo se o fornecedor não
    /// respondeu ou não atende mais o serviço: a margem fica sem esse valor, e o pedido segue.
    /// </summary>
    public async Task<long?> CostAsync(string zipCode, IReadOnlyList<ShippingItem> items, string service, CancellationToken ct)
    {
        try
        {
            return (await SupplierOptionsAsync(zipCode, items, ct)).FirstOrDefault(o => o.Id == service)?.CostCents;
        }
        catch (SupplierUnavailableException)
        {
            return null;
        }
    }

    private async Task<List<ShippingOption>> SupplierOptionsAsync(string zipCode, IReadOnlyList<ShippingItem> items, CancellationToken ct)
    {
        var quoted = await supplier.QuoteFreightAsync(zipCode, [.. items.Select(i => new SupplierOrderItem(i.Sku, i.Sku, i.Quantity))], ct);
        return [.. quoted
            .Where(q => settings.Services.ContainsKey(q.Service))
            .Select(q =>
            {
                var service = settings.Services[q.Service];
                var cents = (long)Math.Ceiling(q.PriceUsdCents * settings.UsdToBrl);
                return new ShippingOption(service.Id, service.Name, cents, q.MinDays + settings.ProcessingDays, q.MaxDays + settings.ProcessingDays, cents);
            })];
    }

    /// <summary>
    /// O que sobra dos produtos para pagar o frete: o preço da loja, menos o custo no fornecedor (no câmbio de agora)
    /// e a taxa do pior caso. Nulo se algum produto não tem preço ou custo conhecido.
    /// </summary>
    private async Task<long?> MarginAsync(IReadOnlyList<ShippingItem> items, IReadOnlyDictionary<Guid, StorePrice> store, CancellationToken ct)
    {
        var supplied = (await products.GetManyAsync(supplier.Name, [.. items.Select(i => i.Sku).Distinct()], ct)).ToDictionary(p => p.Vid);
        long revenue = 0, cost = 0;
        foreach (var item in items)
        {
            if (!store.TryGetValue(item.ProductId!.Value, out var price) || !supplied.TryGetValue(item.Sku, out var product))
            {
                return null;
            }
            var rate = string.Equals(product.Currency, "USD", StringComparison.OrdinalIgnoreCase) ? settings.UsdToBrl : 1m;
            revenue += price.UnitPriceCents * item.Quantity;
            cost += (long)Math.Ceiling(product.CostCents * rate) * item.Quantity;
        }
        var fee = (long)Math.Ceiling(revenue * settings.WorstPaymentFeePercent / 100m);
        return revenue - cost - fee;
    }
}
