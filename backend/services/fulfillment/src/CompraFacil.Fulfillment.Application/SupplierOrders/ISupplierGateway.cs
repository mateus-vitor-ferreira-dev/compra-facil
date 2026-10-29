using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.Application.SupplierOrders;

/// <summary>O pedido como vai ao fornecedor. O número do pedido é a chave de idempotência dele.</summary>
public sealed record SupplierOrderRequest(Guid OrderNumber, string CustomerEmail, IReadOnlyList<SupplierOrderItem> Items, ShipTo? ShipTo);

/// <summary>Resposta da criação: aceito (com o id no fornecedor) ou recusado por regra de negócio.</summary>
public abstract record SupplierPlacement
{
    public sealed record Accepted(string SupplierOrderId) : SupplierPlacement;

    /// <summary>Recusa de negócio; Reason é out_of_stock ou rejected (SupplierFailureReasons).</summary>
    public sealed record Rejected(string Reason, string Detail) : SupplierPlacement;
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

    /// <summary>O feed inteiro (todas as páginas). Falha em qualquer página sai como <see cref="SupplierUnavailableException"/>.</summary>
    Task<IReadOnlyList<SupplierFeedItem>> ListProductsAsync(CancellationToken ct);

    /// <summary>Unidades disponíveis agora de cada vid. Falha em qualquer um sai como <see cref="SupplierUnavailableException"/>.</summary>
    Task<IReadOnlyDictionary<string, int>> GetStockAsync(IReadOnlyCollection<string> vids, CancellationToken ct);
}

public sealed class SupplierUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A partir de quando um pedido confirmado vai ao fornecedor (Supplier:AcceptOrdersSince).</summary>
public sealed record OrderCutoff(DateTimeOffset? Since);
