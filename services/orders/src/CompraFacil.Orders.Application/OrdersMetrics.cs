using System.Diagnostics.Metrics;

namespace CompraFacil.Orders.Application;

/// <summary>Métricas de negócio do orders (meter CompraFacil.Orders).</summary>
public static class OrdersMetrics
{
    private static readonly Meter s_meter = new("CompraFacil.Orders");

    /// <summary>Pedidos criados (início de SAGA).</summary>
    public static readonly Counter<long> Placed = s_meter.CreateCounter<long>(
        "compra_facil.orders.placed", unit: "{order}", description: "Pedidos criados (SAGAs iniciadas).");

    /// <summary>Pedidos finalizados: confirmed = SAGA concluída; cancelled = compensada (por motivo).</summary>
    public static readonly Counter<long> Finished = s_meter.CreateCounter<long>(
        "compra_facil.orders.finished", unit: "{order}", description: "SAGAs finalizadas por status e motivo.");

    /// <summary>Valor dos pedidos confirmados, em centavos.</summary>
    public static readonly Counter<long> ConfirmedAmount = s_meter.CreateCounter<long>(
        "compra_facil.orders.confirmed_amount", unit: "{cent}", description: "Receita dos pedidos confirmados (centavos).");

    /// <summary>Pedidos precificados pela cópia local porque o catalog não respondeu (por motivo).</summary>
    public static readonly Counter<long> CatalogFallback = s_meter.CreateCounter<long>(
        "compra_facil.orders.catalog_fallback", unit: "{order}",
        description: "Precificações pela cópia local do catálogo (timeout, unavailable, circuit_open).");
}
