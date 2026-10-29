using CompraFacil.Inventory.Domain;

namespace CompraFacil.Inventory.Application.Stock;

/// <summary>Tópicos e eventos (contracts/asyncapi/inventory.v1.yaml).</summary>
public static class InventoryEvents
{
    public const string Topic = "inventory.reservation.v1";
    public const string StockReserved = nameof(StockReserved);
    public const string StockReservationFailed = nameof(StockReservationFailed);
    public const string StockReleased = nameof(StockReleased);

    public const string OrdersTopic = "orders.order.v1";
    public const string SupplierProductsTopic = "fulfillment.supplier-product.v1";
    public const string ProductsTopic = "catalog.product.v1";
}

// ---- consumidos (só os campos que o inventory usa; os demais são ignorados) ----

public sealed record OrderLine(Guid ProductId, int Quantity);

public sealed record OrderPlacedData(Guid OrderId, IReadOnlyList<OrderLine> Items);

public sealed record OrderConfirmedData(Guid OrderId);

public sealed record OrderCancelledData(Guid OrderId, string Reason);

/// <summary>Supplier: a variante do fornecedor (nulo no produto da loja, #218).</summary>
public sealed record ProductCreatedData(Guid ProductId, SupplierRefData? Supplier);

public sealed record SupplierRefData(string Name, string Vid);

/// <summary>SupplierStockChanged do fulfillment: quanto o fornecedor tem de uma variante agora.</summary>
public sealed record SupplierStockChangedData(string Supplier, string Vid, int Available, DateTimeOffset CheckedAt);

public sealed record ProductDeletedData(Guid ProductId);

// ---- publicados ----

public sealed record StockReservedData(Guid OrderId, IReadOnlyList<ReservedItem> Items);

public sealed record UnavailableItem(Guid ProductId, int Requested, int Available, UnavailableReason Reason);

public enum UnavailableReason
{
    /// <summary>Saldo menor que o pedido.</summary>
    InsufficientStock,

    /// <summary>Sem saldo cadastrado ou removido do catálogo.</summary>
    UnknownProduct,
}

public sealed record StockReservationFailedData(Guid OrderId, IReadOnlyList<UnavailableItem> UnavailableItems);

public sealed record StockReleasedData(Guid OrderId, IReadOnlyList<ReservedItem> Items);
