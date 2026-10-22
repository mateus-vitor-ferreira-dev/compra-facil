using System.Diagnostics;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Orders.Application;
using CompraFacil.Orders.Application.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using Polly.Retry;
using Polly.Timeout;

namespace CompraFacil.Orders.Infrastructure.Catalog;

/// <summary>
/// O catálogo para o pedido, sem o orders cair junto com o catalog (#81): gRPC com prazo, novas
/// tentativas com jitter e circuit breaker; não deu (ou o circuito está aberto), o preço sai da cópia
/// local (<see cref="SnapshotCatalogPricing"/>). Só se a cópia também não serve o pedido responde 503.
/// </summary>
internal sealed partial class ResilientCatalogPricing(
    CatalogGrpcPricing grpc,
    SnapshotCatalogPricing snapshot,
    ResiliencePipelineProvider<string> pipelines,
    ILogger<ResilientCatalogPricing> logger) : ICatalogPricing
{
    public const string Pipeline = "catalog-grpc";

    /// <summary>Retry-After do 503 quando nem o catalog nem a cópia local servem.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<PricedItem>> PriceAsync(IReadOnlyList<ItemToPrice> items, CancellationToken ct)
    {
        try
        {
            return await pipelines.GetPipeline(Pipeline).ExecuteAsync(
                async token => await grpc.PriceAsync(items, token), ct);
        }
        catch (Exception e) when (Reason(e) is { } reason)
        {
            LogFallback(logger, reason, e.Message);
            Activity.Current?.SetTag("compra_facil.catalog.fallback", reason);
            OrdersMetrics.CatalogFallback.Add(1, new KeyValuePair<string, object?>("reason", reason));
            return await snapshot.PriceAsync(items, ct);
        }
    }

    private static string? Reason(Exception e) => e switch
    {
        BrokenCircuitException => "circuit_open",
        TimeoutRejectedException => "timeout",
        DependencyUnavailableException => "unavailable",
        _ => null,
    };

    /// <summary>
    /// Do lado de fora para dentro: prazo total → novas tentativas → circuit breaker → prazo de cada
    /// tentativa. Circuito aberto não é tentado de novo: vai direto para o fallback.
    /// </summary>
    internal static void Configure(ResiliencePipelineBuilder builder, CatalogOptions options)
    {
        var transient = new PredicateBuilder()
            .Handle<DependencyUnavailableException>()
            .Handle<TimeoutRejectedException>();
        builder
            .AddTimeout(options.TotalTimeout)
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = transient,
                MaxRetryAttempts = options.Retries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.RetryDelay,
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = transient,
                FailureRatio = 0.5,
                MinimumThroughput = options.BreakerMinimumThroughput,
                SamplingDuration = options.BreakerSamplingDuration,
                BreakDuration = options.BreakDuration,
            })
            .AddTimeout(options.AttemptTimeout);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "catálogo fora ({Reason}: {Error}); preço pela cópia local")]
    private static partial void LogFallback(ILogger logger, string reason, string error);
}

internal static class CatalogResilienceExtensions
{
    /// <summary>Cliente gRPC do catalog com a pipeline do Polly e o fallback para o snapshot.</summary>
    public static IServiceCollection AddCatalogPricing(this IServiceCollection services)
    {
        services.AddResiliencePipeline(ResilientCatalogPricing.Pipeline, (builder, context) =>
            ResilientCatalogPricing.Configure(builder, context.ServiceProvider.GetRequiredService<IOptions<CatalogOptions>>().Value));
        services.AddScoped<CatalogGrpcPricing>();
        services.AddScoped<SnapshotCatalogPricing>();
        services.AddScoped<ICatalogPricing, ResilientCatalogPricing>();
        return services;
    }
}
