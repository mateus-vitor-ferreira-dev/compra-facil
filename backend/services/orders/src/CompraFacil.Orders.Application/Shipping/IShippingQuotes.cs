namespace CompraFacil.Orders.Application.Shipping;

/// <summary>Uma opção de envio cotada pelo fulfillment: o serviço, o nome, o preço e o prazo em dias úteis.</summary>
public sealed record ShippingQuote(string Service, string Name, long PriceCents, int MinDays, int MaxDays);

/// <summary>
/// A cotação de frete (#222), pelo fulfillment (POST /v1/shipping/quote). O pedido recota no servidor: o preço
/// e o prazo nunca vêm do navegador. Fora do ar, lança <c>DependencyUnavailableException</c> (503 com
/// Retry-After): sem frete cotado, a loja não promete prazo.
/// </summary>
public interface IShippingQuotes
{
    Task<IReadOnlyList<ShippingQuote>> QuoteAsync(string zipCode, IReadOnlyList<(string Sku, int Quantity)> items, CancellationToken ct);
}
