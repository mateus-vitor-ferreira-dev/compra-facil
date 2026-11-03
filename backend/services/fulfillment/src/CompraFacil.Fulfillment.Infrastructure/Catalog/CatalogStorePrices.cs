using CompraFacil.Contracts.Catalog.V1;
using CompraFacil.Fulfillment.Application.Shipping;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace CompraFacil.Fulfillment.Infrastructure.Catalog;

/// <summary>Seção Catalog: onde o fulfillment busca os preços da loja para o frete grátis (#323).</summary>
public sealed class CatalogPricesOptions
{
    public const string Section = "Catalog";

    /// <summary>Endereço gRPC do catalog (no compose: http://catalog:9001); vazio: sem frete grátis.</summary>
    public Uri? GrpcAddress { get; set; }

    /// <summary>Prazo da consulta: a cotação não espera o catalog mais que isso, e segue sem frete grátis.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Os preços da loja pelo gRPC BatchGetProducts do catalog: o preço cobrado agora (com a campanha) e o SKU. Só
/// os produtos ativos. Qualquer falha devolve nulo: a cotação sai sem frete grátis, e nunca com prejuízo.
/// </summary>
internal sealed partial class CatalogStorePrices(
    CatalogService.CatalogServiceClient client, CatalogPricesOptions options, ILogger<CatalogStorePrices> logger) : IStorePrices
{
    public async Task<IReadOnlyDictionary<Guid, StorePrice>?> GetAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        var request = new BatchGetProductsRequest();
        request.ProductIds.AddRange(productIds.Select(id => id.ToString()));
        try
        {
            var response = await client.BatchGetProductsAsync(request, deadline: DateTime.UtcNow + options.Timeout, cancellationToken: ct);
            return response.Products
                .Where(p => p.Status == ProductStatus.Active && p.EffectivePrice is { Currency: "BRL" })
                .ToDictionary(p => Guid.Parse(p.Id), p => new StorePrice(p.Sku, p.EffectivePrice.AmountCents));
        }
        catch (RpcException e)
        {
            LogUnavailable(logger, e.StatusCode);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "catalog sem resposta ({Status}) para os preços do frete grátis; a cotação segue sem ele")]
    private static partial void LogUnavailable(ILogger logger, StatusCode status);
}

/// <summary>Sem o catalog configurado: sem frete grátis.</summary>
internal sealed class NoStorePrices : IStorePrices
{
    public Task<IReadOnlyDictionary<Guid, StorePrice>?> GetAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, StorePrice>?>(null);
}
