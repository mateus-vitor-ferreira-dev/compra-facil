using System.Net.Http.Json;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Orders.Application.Shipping;

namespace CompraFacil.Orders.Infrastructure.Shipping;

/// <summary>Seção Fulfillment: onde o orders recota o frete (#222).</summary>
public sealed class FulfillmentOptions
{
    public const string Section = "Fulfillment";

    /// <summary>Raiz do fulfillment na rede dos serviços (ex.: http://fulfillment:8007/); vazio: o pedido não sai (503).</summary>
    public Uri? BaseUrl { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>A cotação pelo fulfillment (POST /v1/shipping/quote). Qualquer falha vira 503: sem frete, sem pedido.</summary>
internal sealed class FulfillmentShippingQuotes(HttpClient http) : IShippingQuotes
{
    private static readonly TimeSpan s_retryAfter = TimeSpan.FromSeconds(5);

    // a API da loja é snake_case (contracts/openapi/fulfillment.v1.yaml)
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public async Task<IReadOnlyList<ShippingQuote>> QuoteAsync(string zipCode, IReadOnlyList<(string Sku, int Quantity)> items, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("v1/shipping/quote",
                new QuoteRequest(zipCode, [.. items.Select(i => new QuoteItem(i.Sku, i.Quantity))]), s_json, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new DependencyUnavailableException($"o fulfillment não cotou o frete ({(int)response.StatusCode})", s_retryAfter);
            }
            var body = await response.Content.ReadFromJsonAsync<QuoteResponse>(s_json, ct);
            return [.. (body?.Options ?? []).Select(o => new ShippingQuote(o.Id, o.Name, o.PriceCents, o.MinDays, o.MaxDays))];
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException($"o fulfillment não cotou o frete: {e.Message}", s_retryAfter);
        }
    }

    private sealed record QuoteItem(string Sku, int Quantity);

    private sealed record QuoteRequest(string ZipCode, IReadOnlyList<QuoteItem> Items);

    private sealed record QuoteOption(string Id, string Name, long PriceCents, int MinDays, int MaxDays);

    private sealed record QuoteResponse(IReadOnlyList<QuoteOption>? Options);
}

/// <summary>Sem o fulfillment configurado: o pedido não sai (a loja não promete frete nem prazo sem cotar).</summary>
internal sealed class NoShippingQuotes : IShippingQuotes
{
    public Task<IReadOnlyList<ShippingQuote>> QuoteAsync(string zipCode, IReadOnlyList<(string Sku, int Quantity)> items, CancellationToken ct) =>
        throw new DependencyUnavailableException("a cotação de frete não está configurada (Fulfillment:BaseUrl)", TimeSpan.FromSeconds(30));
}
