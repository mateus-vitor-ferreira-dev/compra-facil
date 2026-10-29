using System.Diagnostics.Metrics;

namespace CompraFacil.Inventory.Application;

/// <summary>Métricas de negócio do inventory (meter CompraFacil.Inventory).</summary>
public static class InventoryMetrics
{
    private static readonly Meter s_meter = new("CompraFacil.Inventory");

    /// <summary>Resultado das reservas (reserved, failed) e liberações (released).</summary>
    public static readonly Counter<long> Reservations = s_meter.CreateCounter<long>(
        "compra_facil.inventory.reservations", unit: "{reservation}", description: "Reservas por resultado.");

    /// <summary>Leituras do estoque do fornecedor aplicadas, pela origem (event, live, fallback).</summary>
    public static readonly Counter<long> SupplierStock = s_meter.CreateCounter<long>(
        "compra_facil.inventory.supplier_stock", unit: "{reading}", description: "Leituras do estoque do fornecedor, por origem.");
}
