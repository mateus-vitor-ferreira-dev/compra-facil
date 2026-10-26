namespace CompraFacil.Inventory.Domain;

/// <summary>
/// Saldo de um produto. <c>Available = OnHand - Reserved</c>: o que ainda pode ser vendido. Nenhuma
/// operação deixa OnHand, Reserved ou Available negativos. Concorrência otimista pelo xmin: duas
/// reservas simultâneas no mesmo produto não passam juntas sem uma reler o saldo.
/// </summary>
public sealed class StockItem
{
    private StockItem()
    {
        // EF Core
    }

    public Guid ProductId { get; private set; }

    /// <summary>Unidades fisicamente em estoque.</summary>
    public int OnHand { get; private set; }

    /// <summary>Unidades presas a pedidos pendentes.</summary>
    public int Reserved { get; private set; }

    /// <summary>Removido do catálogo: não aceita reserva nova (as existentes seguem).</summary>
    public bool Discontinued { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public int Available => OnHand - Reserved;

    public static StockItem Create(Guid productId, int onHand, DateTimeOffset now)
    {
        if (onHand < 0)
        {
            throw new DomainException("estoque inicial negativo");
        }
        return new StockItem { ProductId = productId, OnHand = onHand, UpdatedAt = now };
    }

    public bool CanReserve(int quantity) => !Discontinued && quantity > 0 && Available >= quantity;

    public void Reserve(int quantity, DateTimeOffset now)
    {
        if (!CanReserve(quantity))
        {
            throw new DomainException($"produto {ProductId}: não há {quantity} disponíveis (disponível {Available})");
        }
        Reserved += quantity;
        UpdatedAt = now;
    }

    /// <summary>Compensação: devolve ao disponível o que estava reservado.</summary>
    public void Release(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0 || quantity > Reserved)
        {
            throw new DomainException($"produto {ProductId}: liberar {quantity} com {Reserved} reservados");
        }
        Reserved -= quantity;
        UpdatedAt = now;
    }

    /// <summary>Pedido confirmado: a reserva vira baixa definitiva.</summary>
    public void Commit(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0 || quantity > Reserved || quantity > OnHand)
        {
            throw new DomainException($"produto {ProductId}: baixar {quantity} com {Reserved} reservados e {OnHand} em estoque");
        }
        Reserved -= quantity;
        OnHand -= quantity;
        UpdatedAt = now;
    }

    /// <summary>Ajuste do estoque físico (entrada, inventário, avaria). Não pode ficar abaixo do reservado.</summary>
    public void Adjust(int delta, DateTimeOffset now)
    {
        if (delta == 0)
        {
            throw new DomainException("ajuste sem efeito (delta 0)");
        }
        var onHand = (long)OnHand + delta;
        if (onHand < Reserved)
        {
            throw new DomainException(
                $"produto {ProductId}: o estoque ficaria em {onHand}, abaixo dos {Reserved} já reservados");
        }
        OnHand = checked((int)onHand);
        UpdatedAt = now;
    }

    public void Discontinue(DateTimeOffset now)
    {
        Discontinued = true;
        UpdatedAt = now;
    }
}
