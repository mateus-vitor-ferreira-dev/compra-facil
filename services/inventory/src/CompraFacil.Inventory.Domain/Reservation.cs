namespace CompraFacil.Inventory.Domain;

public enum ReservationStatus
{
    /// <summary>Aguardando o pagamento.</summary>
    Reserved,

    /// <summary>Pedido confirmado: virou baixa.</summary>
    Committed,

    /// <summary>Devolvida ao estoque (pedido cancelado).</summary>
    Released,
}

public sealed record ReservedItem(Guid ProductId, int Quantity);

/// <summary>Reserva de um pedido: todos os itens ou nenhum. Uma por pedido.</summary>
public sealed class Reservation
{
    private List<ReservedItem> _items = [];

    private Reservation()
    {
        // EF Core
    }

    public Guid OrderId { get; private set; }
    public ReservationStatus Status { get; private set; }
    public IReadOnlyList<ReservedItem> Items => _items;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static Reservation Create(Guid orderId, IReadOnlyList<ReservedItem> items, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new DomainException("reserva sem itens");
        }
        var reservation = new Reservation { OrderId = orderId, Status = ReservationStatus.Reserved, CreatedAt = now, UpdatedAt = now };
        reservation._items = [.. items];
        return reservation;
    }

    public void Commit(DateTimeOffset now) => MoveTo(ReservationStatus.Committed, now);

    public void Release(DateTimeOffset now) => MoveTo(ReservationStatus.Released, now);

    private void MoveTo(ReservationStatus target, DateTimeOffset now)
    {
        if (Status != ReservationStatus.Reserved)
        {
            throw new DomainException($"reserva do pedido {OrderId} já está {Status}");
        }
        Status = target;
        UpdatedAt = now;
    }
}
