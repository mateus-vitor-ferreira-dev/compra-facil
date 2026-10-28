namespace CompraFacil.Fulfillment.Infrastructure.Supplier;

/// <summary>
/// Seção Supplier: o fornecedor e o despachante. Em dev, o fornecedor simulado do compose
/// (backend/services/supplier-sim), que fala o contrato do CJ Dropshipping.
/// </summary>
public sealed class SupplierOptions
{
    /// <summary>Nome do fornecedor nos eventos e no banco.</summary>
    public string Name { get; set; } = "cj";

    /// <summary>Raiz da API, com a barra no fim (ex.: http://supplier-sim:8090/api2.0/v1/).</summary>
    public Uri BaseUrl { get; set; } = new("http://localhost:8090/api2.0/v1/");

    public string Email { get; set; } = "sim@comprafacil.local";
    public string Password { get; set; } = "sim";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Segredo do HMAC dos webhooks (o mesmo configurado no fornecedor).</summary>
    public string WebhookSecret { get; set; } = "sim-secret";

    /// <summary>Cabeçalho com a assinatura do webhook ("sha256=&lt;hex&gt;").</summary>
    public string SignatureHeader { get; set; } = "X-Sim-Signature";

    /// <summary>
    /// Só com o simulado: o cenário vem do e-mail do cliente (ana+recusar@... vira "[recusar]" na
    /// observação do pedido), para demonstrar recusa, cancelamento e atraso pela própria loja.
    /// </summary>
    public bool ScenarioFromEmail { get; set; }

    public bool DispatcherEnabled { get; set; } = true;

    /// <summary>
    /// Data de corte: OrderConfirmed anterior a ela não vai ao fornecedor. O consumer lê o tópico desde o
    /// começo (para não perder evento), e um fulfillment novo mandaria ao fornecedor os pedidos antigos
    /// ainda guardados no Kafka. Obrigatória em produção (o serviço não sobe sem ela); vazia, aceita tudo.
    /// </summary>
    public DateTimeOffset? AcceptOrdersSince { get; set; }
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public int BatchSize { get; set; } = 10;

    /// <summary>Quanto um pedido reservado (Placing) espera antes de outro despachante reenviar.</summary>
    public TimeSpan ClaimLease { get; set; } = TimeSpan.FromMinutes(2);
}
