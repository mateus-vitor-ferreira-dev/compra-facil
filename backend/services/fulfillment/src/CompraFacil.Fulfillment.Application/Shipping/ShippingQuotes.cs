using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application.Shipping;

/// <summary>Um item a cotar: o SKU da loja (o vid do fornecedor) e a quantidade.</summary>
public sealed record ShippingItem(string Sku, int Quantity);

/// <summary>
/// Uma opção de envio como o cliente vê: o serviço, o preço em reais e o prazo em dias úteis, já com o
/// processamento da loja somado ao do fornecedor.
/// </summary>
public sealed record ShippingOption(string Id, string Name, long PriceCents, int MinDays, int MaxDays);

/// <summary>Seção Shipping: o câmbio do frete (o do catálogo), o processamento da loja e os nomes dos serviços.</summary>
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
}

public sealed class ShippingService
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// A cotação de frete e prazo por CEP (#222, Decreto 7.962: antes de pagar). Pergunta ao fornecedor, converte
/// o preço para reais (arredondado para cima, no centavo) e soma o processamento da loja ao prazo. As opções
/// saem da mais barata para a mais cara.
/// </summary>
public sealed class ShippingQuotes(ISupplierGateway supplier, ShippingSettings settings)
{
    public const int MaxItems = 50;

    /// <exception cref="SupplierUnavailableException">O fornecedor não respondeu.</exception>
    public async Task<IReadOnlyList<ShippingOption>> QuoteAsync(string zipCode, IReadOnlyList<ShippingItem> items, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(items);
        var quoted = await supplier.QuoteFreightAsync(zipCode, [.. items.Select(i => new SupplierOrderItem(i.Sku, i.Sku, i.Quantity))], ct);
        return [.. quoted
            .Where(q => settings.Services.ContainsKey(q.Service))
            .Select(q =>
            {
                var service = settings.Services[q.Service];
                var cents = (long)Math.Ceiling(q.PriceUsdCents * settings.UsdToBrl);
                return new ShippingOption(service.Id, service.Name, cents, q.MinDays + settings.ProcessingDays, q.MaxDays + settings.ProcessingDays);
            })
            .OrderBy(o => o.PriceCents)];
    }
}
