using System.Diagnostics.Metrics;

namespace CompraFacil.Payments.Application;

/// <summary>Métricas de negócio do payments (meter CompraFacil.Payments).</summary>
public static class PaymentsMetrics
{
    public const string MeterName = "CompraFacil.Payments";
    private static readonly Meter s_meter = new(MeterName);

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

    /// <summary>
    /// Cartões aprovados pela operadora que não se confirmaram antes da liquidação (failed: não pago; voided:
    /// cancelado): o dinheiro não vem, e o pedido é parado ou vai para a equipe.
    /// </summary>
    public static readonly Counter<long> Reversals = s_meter.CreateCounter<long>(
        "compra_facil.payments.reversals", unit: "{payment}", description: "Cartões aprovados que a operadora não confirmou, por status.");

    /// <summary>Pagamentos contestados pelo cliente (reclamação ou chargeback), que a equipe responde no provedor.</summary>
    public static readonly Counter<long> Disputes = s_meter.CreateCounter<long>(
        "compra_facil.payments.disputes", unit: "{dispute}", description: "Pagamentos contestados (reclamação ou chargeback).");

    /// <summary>
    /// Avisos da Efí recusados ou que não são desta loja, por webhook (pix, charges) e motivo: secret (o segredo do
    /// PIX errado), invalid (sem o token) e unknown_token (token que a Efí não conhece). Rajada é alguém forjando
    /// avisos, ou a URL cadastrada na Efí desatualizada.
    /// </summary>
    private static readonly Counter<long> s_webhooksRejected = s_meter.CreateCounter<long>(
        "compra_facil.payments.webhooks_rejected", unit: "{request}", description: "Avisos da Efí recusados, por webhook e motivo.");

    public static void WebhookRejected(string webhook, string reason) =>
        s_webhooksRejected.Add(1, new KeyValuePair<string, object?>("webhook", webhook), new KeyValuePair<string, object?>("reason", reason));
}
