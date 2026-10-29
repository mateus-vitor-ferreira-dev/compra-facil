using System.Diagnostics.Metrics;

namespace CompraFacil.Payments.Application;

/// <summary>Métricas de negócio do payments (meter CompraFacil.Payments).</summary>
public static class PaymentsMetrics
{
    private static readonly Meter s_meter = new("CompraFacil.Payments");

    /// <summary>Cobranças por resultado (approved, failed) e motivo da recusa.</summary>
    public static readonly Counter<long> Charges = s_meter.CreateCounter<long>(
        "compra_facil.payments.charges", unit: "{charge}", description: "Cobranças por resultado e motivo.");

    /// <summary>Estornos de pedidos cancelados depois de pagos.</summary>
    public static readonly Counter<long> Refunds = s_meter.CreateCounter<long>(
        "compra_facil.payments.refunds", unit: "{refund}", description: "Estornos de pedidos cancelados depois de pagos.");
}
