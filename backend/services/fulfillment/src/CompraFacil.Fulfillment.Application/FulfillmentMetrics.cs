using System.Diagnostics.Metrics;
using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application;

/// <summary>Métricas do fulfillment (meter CompraFacil.Fulfillment, exportado pelo ServiceDefaults).</summary>
public static class FulfillmentMetrics
{
    public const string MeterName = "CompraFacil.Fulfillment";
    private static readonly Meter s_meter = new(MeterName);

    /// <summary>Tentativas de levar um pedido ao fornecedor, pelo status em que ele ficou.</summary>
    private static readonly Counter<long> s_placements = s_meter.CreateCounter<long>(
        "compra_facil.fulfillment.placements", unit: "{order}", description: "Pedidos levados ao fornecedor, por resultado.");

    /// <summary>Pedidos confirmados antes da data de corte, que não foram ao fornecedor.</summary>
    private static readonly Counter<long> s_skipped = s_meter.CreateCounter<long>(
        "compra_facil.fulfillment.skipped_before_cutoff", unit: "{order}", description: "Pedidos anteriores ao corte, ignorados.");

    /// <summary>Produtos do feed do fornecedor em cada sincronização, pelo que aconteceu com eles.</summary>
    private static readonly Counter<long> s_catalog = s_meter.CreateCounter<long>(
        "compra_facil.fulfillment.catalog_products", unit: "{product}", description: "Produtos do feed por sincronização (created, updated, removed, unchanged).");

    public static void SkippedBeforeCutoff() => s_skipped.Add(1);

    public static void CatalogSynced(string supplier, CatalogSyncResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        foreach (var (change, n) in new[] { ("created", result.Created), ("updated", result.Updated), ("removed", result.Removed), ("unchanged", result.Unchanged) })
        {
            s_catalog.Add(n, new KeyValuePair<string, object?>("supplier", supplier), new KeyValuePair<string, object?>("change", change));
        }
    }

    public static void Placement(string supplier, SupplierOrderStatus result) =>
        s_placements.Add(1, new KeyValuePair<string, object?>("supplier", supplier), new KeyValuePair<string, object?>("result", result.ToString()));
}
