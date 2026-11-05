using System.Diagnostics.Metrics;

namespace CompraFacil.Payments.Application;

/// <summary>Métricas de negócio do payments (meter CompraFacil.Payments).</summary>
public static class PaymentsMetrics
{
    private static readonly Meter s_meter = new("CompraFacil.Payments");

    /// <summary>Cobranças por resultado (approved, failed) e motivo da recusa.</summary>
    public static readonly Counter<long> Charges = s_meter.CreateCounter<long>(
        "compra_facil.payments.charges", unit: "{charge}", description: "Cobranças por meio, resultado e motivo.");

    /// <summary>
    /// Dinheiro de volta: refund (estorno do cancelamento ou do arrependimento), partial (do painel) e deferred (a
    /// devolução do cartão aprovado que espera a liquidação).
    /// </summary>
    public static readonly Counter<long> Refunds = s_meter.CreateCounter<long>(
        "compra_facil.payments.refunds", unit: "{refund}", description: "Estornos e devoluções à espera da liquidação, por tipo.");

    /// <summary>Estornos que o provedor aceitou e não realizou (#396): a equipe precisa devolver de novo.</summary>
    public static readonly Counter<long> RefundsFailed = s_meter.CreateCounter<long>(
        "compra_facil.payments.refunds_failed", unit: "{refund}", description: "Estornos aceitos pelo provedor e não realizados.");

    /// <summary>Pagamentos contestados pelo cliente (reclamação ou chargeback), que a equipe responde no provedor.</summary>
    public static readonly Counter<long> Disputes = s_meter.CreateCounter<long>(
        "compra_facil.payments.disputes", unit: "{dispute}", description: "Pagamentos contestados (reclamação ou chargeback).");
}
