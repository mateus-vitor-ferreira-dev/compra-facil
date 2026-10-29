using CompraFacil.Inventory.Domain;

namespace CompraFacil.Inventory.UnitTests;

public sealed class StockItemTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static StockItem Stock(int onHand) => StockItem.Create(Guid.NewGuid(), onHand, s_now);

    [Fact]
    public void Reserve_MovesFromAvailableToReserved()
    {
        var stock = Stock(10);

        stock.Reserve(4, s_now);

        Assert.Equal((10, 4, 6), (stock.OnHand, stock.Reserved, stock.Available));
    }

    [Fact]
    public void Reserve_NeverGoesBelowZero()
    {
        var stock = Stock(3);
        stock.Reserve(3, s_now);

        Assert.False(stock.CanReserve(1));
        Assert.Throws<DomainException>(() => stock.Reserve(1, s_now));
        Assert.Equal(0, stock.Available);
    }

    [Fact]
    public void Discontinued_RefusesNewReservations()
    {
        var stock = Stock(10);
        stock.Discontinue(s_now);

        Assert.False(stock.CanReserve(1));
        Assert.Throws<DomainException>(() => stock.Reserve(1, s_now));
    }

    [Fact]
    public void Release_ReturnsToAvailable()
    {
        var stock = Stock(5);
        stock.Reserve(5, s_now);

        stock.Release(2, s_now);

        Assert.Equal((5, 3, 2), (stock.OnHand, stock.Reserved, stock.Available));
        Assert.Throws<DomainException>(() => stock.Release(4, s_now));
    }

    [Fact]
    public void Commit_LowersOnHandAndReserved()
    {
        var stock = Stock(5);
        stock.Reserve(2, s_now);

        stock.Commit(2, s_now);

        Assert.Equal((3, 0, 3), (stock.OnHand, stock.Reserved, stock.Available));
        Assert.Throws<DomainException>(() => stock.Commit(1, s_now));
    }

    [Fact]
    public void Adjust_CannotGoBelowReserved()
    {
        var stock = Stock(10);
        stock.Reserve(6, s_now);

        stock.Adjust(-4, s_now);
        Assert.Equal(6, stock.OnHand);

        Assert.Throws<DomainException>(() => stock.Adjust(-1, s_now));
        Assert.Throws<DomainException>(() => stock.Adjust(0, s_now));
        Assert.Equal(6, stock.OnHand);
    }

    [Fact]
    public void Reservation_MovesOnlyFromReserved()
    {
        var reservation = Reservation.Create(Guid.NewGuid(), [new ReservedItem(Guid.NewGuid(), 1)], s_now);

        reservation.Release(s_now);

        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.Throws<DomainException>(() => reservation.Commit(s_now));
        Assert.Throws<DomainException>(() => Reservation.Create(Guid.NewGuid(), [], s_now));
    }

    [Fact]
    public void Mirror_MakesAvailableTheSupplierStock_KeepingTheReservations()
    {
        var item = StockItem.Create(Guid.NewGuid(), 0, s_now);
        Assert.Throws<DomainException>(() => item.Mirror(5, s_now)); // estoque próprio não espelha
        item.LinkToSupplier("cj", "V-1", s_now);

        Assert.True(item.Mirror(5, s_now));
        item.Reserve(2, s_now);
        Assert.Equal(3, item.Available);
        Assert.True(item.Mirror(4, s_now)); // o fornecedor tem 4: as 2 reservadas daqui ainda não chegaram lá
        Assert.Equal((6, 2, 4), (item.OnHand, item.Reserved, item.Available));
        Assert.False(item.Mirror(4, s_now));
        Assert.Throws<DomainException>(() => item.Adjust(1, s_now));
    }

    [Fact]
    public void SupplierLevel_IgnoresAnOlderReading()
    {
        var level = SupplierStockLevel.Create("cj", "V-1", 5, s_now);
        Assert.False(level.Apply(9, s_now.AddMinutes(-1)));
        Assert.True(level.Apply(0, s_now.AddMinutes(1)));
        Assert.Equal(0, level.Available);
    }
}
