using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application.SupplierOrders;

/// <summary>
/// O pedido como vai ao fornecedor. O número do pedido é a chave de idempotência dele. Na venda à ordem, a
/// nota da loja vai junto (a chave e o XML), para o fornecedor emitir a remessa referenciando-a (#221).
/// </summary>
public sealed record SupplierOrderRequest(
    Guid OrderNumber, string CustomerEmail, IReadOnlyList<SupplierOrderItem> Items, ShipTo? ShipTo,
    string? InvoiceKey = null, string? InvoiceXml = null);

/// <summary>Resposta da criação: aceito (com o id no fornecedor) ou recusado por regra de negócio.</summary>
public abstract record SupplierPlacement
{
    public sealed record Accepted(string SupplierOrderId) : SupplierPlacement;

    /// <summary>Recusa de negócio; Reason é out_of_stock ou rejected (SupplierFailureReasons).</summary>
    public sealed record Rejected(string Reason, string Detail) : SupplierPlacement;
}

/// <summary>Resposta da abertura da devolução: aberta (com a disputa, o código e o endereço) ou recusada.</summary>
public abstract record SupplierReturnOpening
{
    public sealed record Opened(string DisputeId, string ReturnCode, string ReturnAddress) : SupplierReturnOpening;

    public sealed record Refused(string Reason) : SupplierReturnOpening;
}

/// <summary>
/// O fornecedor (ADR 0007): uma implementação por fornecedor, atrás da mesma porta. Erro passageiro
/// (fora do ar, timeout, 5xx) sai como <see cref="SupplierUnavailableException"/>: o pedido volta para a
/// fila; recusa de negócio vem como <see cref="SupplierPlacement.Rejected"/> e não tenta de novo.
/// </summary>
public interface ISupplierGateway
{
    /// <summary>Nome do fornecedor nos eventos e no banco (ex.: cj).</summary>
    string Name { get; }

    Task<SupplierPlacement> PlaceOrderAsync(SupplierOrderRequest request, CancellationToken ct);

    /// <summary>
    /// Abre a devolução de um pedido entregue (uma disputa, no CJ). Repetir devolve a mesma. Recusa de negócio
    /// vem como <see cref="SupplierReturnOpening.Refused"/>; erro passageiro, como <see cref="SupplierUnavailableException"/>.
    /// </summary>
    Task<SupplierReturnOpening> OpenReturnAsync(Guid orderNumber, string customerEmail, IReadOnlyList<SupplierOrderItem> items, CancellationToken ct);

    /// <summary>
    /// Cancela um pedido ainda não despachado. False se ele já saiu (nada muda). Falha sai como
    /// <see cref="SupplierUnavailableException"/>.
    /// </summary>
    Task<bool> CancelOrderAsync(string supplierOrderId, CancellationToken ct);

    /// <summary>O feed inteiro (todas as páginas). Falha em qualquer página sai como <see cref="SupplierUnavailableException"/>.</summary>
    Task<IReadOnlyList<SupplierFeedItem>> ListProductsAsync(CancellationToken ct);

    /// <summary>
    /// As opções de envio do fornecedor ao CEP, com o preço em centavos de dólar e o prazo em dias úteis.
    /// Falha sai como <see cref="SupplierUnavailableException"/>.
    /// </summary>
    Task<IReadOnlyList<SupplierFreightOption>> QuoteFreightAsync(string zipCode, IReadOnlyList<SupplierOrderItem> items, CancellationToken ct);

    /// <summary>Unidades disponíveis agora de cada vid. Falha em qualquer um sai como <see cref="SupplierUnavailableException"/>.</summary>
    Task<IReadOnlyDictionary<string, int>> GetStockAsync(IReadOnlyCollection<string> vids, CancellationToken ct);
}

/// <summary>Uma opção de envio do fornecedor: o serviço dele, o preço em centavos de dólar e o prazo em dias úteis.</summary>
public sealed record SupplierFreightOption(string Service, long PriceUsdCents, int MinDays, int MaxDays);

public sealed class SupplierUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A partir de quando um pedido confirmado vai ao fornecedor (Supplier:AcceptOrdersSince).</summary>
public sealed record OrderCutoff(DateTimeOffset? Since);
