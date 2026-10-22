using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Contracts.Catalog.V1;
using CompraFacil.Orders.Application.Catalog;
using Grpc.Core;
using DomainMoney = CompraFacil.Orders.Domain.Money;

namespace CompraFacil.Orders.Infrastructure.Catalog;

public sealed class CatalogOptions
{
    /// <summary>Endereço gRPC do catalog (no compose: http://catalog:9001).</summary>
    public Uri GrpcAddress { get; set; } = new("http://localhost:9001");

    /// <summary>Prazo de cada tentativa.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Prazo total, com as novas tentativas; estourou, o pedido vai para o snapshot.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Novas tentativas depois da primeira (backoff exponencial com jitter).</summary>
    public int Retries { get; set; } = 2;

    /// <summary>Atraso base entre tentativas.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Circuit breaker: com pelo menos <see cref="BreakerMinimumThroughput"/> chamadas em
    /// <see cref="BreakerSamplingDuration"/> e metade falhando, abre por <see cref="BreakDuration"/>:
    /// nesse tempo os pedidos vão direto para o snapshot, sem esperar o catalog.
    /// </summary>
    public int BreakerMinimumThroughput { get; set; } = 10;

    public TimeSpan BreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// ICatalogPricing pelo gRPC ValidateItems do catalog (contracts/proto/catalog/v1), uma tentativa.
/// Falha de transporte vira <see cref="DependencyUnavailableException"/>; prazo, retry, circuit
/// breaker e fallback ficam no <see cref="ResilientCatalogPricing"/>.
/// </summary>
internal sealed class CatalogGrpcPricing(CatalogService.CatalogServiceClient client) : ICatalogPricing
{
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
            response = await client.ValidateItemsAsync(request, cancellationToken: ct);
        }
        catch (RpcException e) when (e.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
            or StatusCode.ResourceExhausted or StatusCode.Internal or StatusCode.Unknown)
        {
            throw new DependencyUnavailableException($"catálogo indisponível ({e.StatusCode}) para validar os itens",
                ResilientCatalogPricing.RetryAfter);
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
