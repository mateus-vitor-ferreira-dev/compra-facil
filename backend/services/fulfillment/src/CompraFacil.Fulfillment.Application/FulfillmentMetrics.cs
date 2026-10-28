using System.Diagnostics.Metrics;
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

    public static void SkippedBeforeCutoff() => s_skipped.Add(1);

    public static void Placement(string supplier, SupplierOrderStatus result) =>
        s_placements.Add(1, new KeyValuePair<string, object?>("supplier", supplier), new KeyValuePair<string, object?>("result", result.ToString()));
}
