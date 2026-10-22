using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Contracts.Catalog.V1;
using CompraFacil.Orders.Application.Catalog;
using Grpc.Core;
using Microsoft.Extensions.Options;
using DomainMoney = CompraFacil.Orders.Domain.Money;

namespace CompraFacil.Orders.Infrastructure.Catalog;

public sealed class CatalogOptions
{
    /// <summary>Endereço gRPC do catalog (no compose: http://catalog:9001).</summary>
    public Uri GrpcAddress { get; set; } = new("http://localhost:9001");

    /// <summary>Prazo de cada chamada; estourou, o pedido responde 503.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// ICatalogPricing pelo gRPC ValidateItems do catalog (contracts/proto/catalog/v1). Catálogo fora
/// ou lento vira 503 com Retry-After; retry, circuit breaker e fallback para o snapshot: #81.
/// </summary>
internal sealed class CatalogGrpcPricing(CatalogService.CatalogServiceClient client, IOptions<CatalogOptions> options) : ICatalogPricing
{
    private static readonly TimeSpan s_retryAfter = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<PricedItem>> PriceAsync(IReadOnlyList<ItemToPrice> items, CancellationToken ct)
    {
        var request = new ValidateItemsRequest();
        foreach (var item in items)
        {
            var proto = new ItemToValidate { ProductId = item.ProductId.ToString(), Quantity = item.Quantity };
            if (item.ExpectedUnitPriceCents is { } expected)
            {
                proto.ExpectedUnitPriceCents = expected;
            }
            request.Items.Add(proto);
        }

        ValidateItemsResponse response;
        try
        {
            response = await client.ValidateItemsAsync(request,
                deadline: DateTime.UtcNow.Add(options.Value.Timeout), cancellationToken: ct);
        }
        catch (RpcException e) when (e.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
            or StatusCode.ResourceExhausted or StatusCode.Internal or StatusCode.Unknown)
        {
            throw new DependencyUnavailableException($"catálogo indisponível ({e.StatusCode}) para validar os itens", s_retryAfter);
        }

        return [.. response.Items.Select(i => new PricedItem(
            Guid.Parse(i.ProductId),
            Map(i.Status),
            i.Quantity,
            i.Sku,
            i.Name,
            i.UnitPrice is null ? null : new DomainMoney(i.UnitPrice.AmountCents, i.UnitPrice.Currency),
            string.IsNullOrEmpty(i.CampaignId) ? null : Guid.Parse(i.CampaignId)))];
    }

    private static PricedItemStatus Map(ItemStatus status) => status switch
    {
        ItemStatus.Ok => PricedItemStatus.Ok,
        ItemStatus.NotFound => PricedItemStatus.NotFound,
        ItemStatus.NotActive => PricedItemStatus.NotActive,
        ItemStatus.PriceChanged => PricedItemStatus.PriceChanged,
        ItemStatus.InvalidQuantity => PricedItemStatus.InvalidQuantity,
        _ => throw new InvalidOperationException($"status de item desconhecido do catálogo: {status}"),
    };
}
