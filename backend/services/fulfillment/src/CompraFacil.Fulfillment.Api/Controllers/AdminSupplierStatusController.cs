using System.Diagnostics;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Infrastructure.Supplier;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CompraFacil.Fulfillment.Api.Controllers;

/// <summary>
/// getSupplierStatus (contracts/openapi/fulfillment.v1.yaml): como está a integração com o fornecedor (#225). As
/// credenciais ficam nos segredos do ambiente: daqui só sai se são as do simulador, nunca o valor delas.
/// </summary>
[ApiController]
[Produces("application/json")]
[Authorize(Policy = JwtAuth.AdminPolicy)]
public sealed class AdminSupplierStatusController(
    ISupplierGateway supplier, ISupplierProductRepository products, ISupplierOrderRepository orders,
    IOptions<SupplierOptions> options, IOptions<SupplierDeadlines> deadlines) : ControllerBase
{
    // a tela não espera os 15 s de timeout de um fornecedor fora do ar
    private static readonly TimeSpan s_probeTimeout = TimeSpan.FromSeconds(3);

    [HttpGet("v1/admin/supplier/status")]
    public async Task<SupplierStatus> Get(CancellationToken ct)
    {
        var o = options.Value;
        var d = deadlines.Value;
        var (reachable, latency, error) = await ProbeAsync(ct);
        var (inFeed, lastStock) = await products.FeedSummaryAsync(supplier.Name, ct);
        return new SupplierStatus(
            supplier.Name, o.BaseUrl.Host, reachable, latency, error,
            o.Email != SupplierOptions.SimulatorEmail, !string.IsNullOrEmpty(o.WebhookSecret),
            inFeed, lastStock, await orders.LastWebhookAtAsync(ct),
            d.ShipWithin.TotalHours, d.DeliverWithin.TotalHours, d.ReturnWithin.TotalHours);
    }

    private async Task<(bool Reachable, long? LatencyMs, string? Error)> ProbeAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(s_probeTimeout);
        var watch = Stopwatch.StartNew();
        try
        {
            await supplier.CheckAsync(timeout.Token);
            return (true, watch.ElapsedMilliseconds, null);
        }
        catch (SupplierUnavailableException e)
        {
            return (false, null, e.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, null, $"o fornecedor não respondeu em {s_probeTimeout.TotalSeconds:0} s");
        }
    }
}

public sealed record SupplierStatus(
    string Name,
    string Host,
    bool Reachable,
    long? LatencyMs,
    string? Error,
    bool OwnCredentials,
    bool WebhookSecretConfigured,
    int ProductsInFeed,
    DateTimeOffset? LastStockReadAt,
    DateTimeOffset? LastWebhookAt,
    double ShipWithinHours,
    double DeliverWithinHours,
    double ReturnWithinHours);
