using System.Diagnostics.Metrics;

namespace Embrulhado.Orders.Application;

/// <summary>Métricas de negócio do orders (meter Embrulhado.Orders).</summary>
public static class OrdersMetrics
{
    private static readonly Meter s_meter = new("Embrulhado.Orders");

    /// <summary>Pedidos criados (início de SAGA).</summary>
    public static readonly Counter<long> Placed = s_meter.CreateCounter<long>(
        "embrulhado.orders.placed", unit: "{order}", description: "Pedidos criados (SAGAs iniciadas).");

    /// <summary>Pedidos finalizados: confirmed = SAGA concluída; cancelled = compensada (por motivo).</summary>
    public static readonly Counter<long> Finished = s_meter.CreateCounter<long>(
        "embrulhado.orders.finished", unit: "{order}", description: "SAGAs finalizadas por status e motivo.");

    /// <summary>Passos depois do pagamento (fornecedor e estorno), pelo evento que os causou.</summary>
    public static readonly Counter<long> Progressed = s_meter.CreateCounter<long>(
        "embrulhado.orders.progressed", unit: "{order}", description: "Pedidos que andaram depois de pagos, por etapa.");

    /// <summary>Valor dos pedidos confirmados, em centavos.</summary>
    public static readonly Counter<long> ConfirmedAmount = s_meter.CreateCounter<long>(
        "embrulhado.orders.confirmed_amount", unit: "{cent}", description: "Receita dos pedidos confirmados (centavos).");

    /// <summary>Pedidos precificados pela cópia local porque o catalog não respondeu (por motivo).</summary>
    public static readonly Counter<long> CatalogFallback = s_meter.CreateCounter<long>(
        "embrulhado.orders.catalog_fallback", unit: "{order}",
        description: "Precificações pela cópia local do catálogo (timeout, unavailable, circuit_open).");
}
