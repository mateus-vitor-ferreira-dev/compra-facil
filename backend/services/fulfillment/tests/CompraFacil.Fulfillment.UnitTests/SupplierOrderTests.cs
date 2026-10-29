using CompraFacil.Fulfillment.Domain;

namespace CompraFacil.Fulfillment.UnitTests;

public sealed class SupplierOrderTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static SupplierOrder NewOrder() =>
        SupplierOrder.Create(Guid.NewGuid(), "ana@x.com", [new SupplierOrderItem("MOU-000001", "Mouse", 2)], "cj", s_now);

    [Fact]
    public void Caminho_feliz_anda_ate_entregue()
    {
        var order = NewOrder();

        Assert.Equal(SupplierOrderChange.Placed, order.MarkPlaced("CJ1", s_now));
        Assert.Equal(SupplierOrderChange.Shipped, order.MarkShipped(new Tracking("BR123", "Correios", null), s_now));
        Assert.Equal(SupplierOrderChange.Delivered, order.MarkDelivered(s_now));

        Assert.Equal(SupplierOrderStatus.Delivered, order.Status);
        Assert.Equal("CJ1", order.SupplierOrderId);
        Assert.Equal("BR123", order.Tracking!.Number);
    }

    [Fact]
    public void Webhook_atrasado_ou_repetido_nao_faz_voltar()
    {
        var order = NewOrder();
        order.MarkShipped(new Tracking("BR123", null, null), s_now);
        order.MarkDelivered(s_now);

        Assert.Equal(SupplierOrderChange.None, order.MarkPlaced("CJ1", s_now));
        Assert.Equal(SupplierOrderChange.None, order.MarkShipped(new Tracking("BR999", null, null), s_now));
        Assert.Equal(SupplierOrderChange.None, order.MarkDelivered(s_now));
        Assert.Equal(SupplierOrderChange.None, order.Fail(SupplierFailureReasons.SupplierCancelled, null, s_now));
        Assert.Equal(SupplierOrderStatus.Delivered, order.Status);
        Assert.Equal("BR123", order.Tracking!.Number);
    }

    [Fact]
    public void Enviado_nao_vira_recusado()
    {
        var order = NewOrder();
        order.MarkShipped(new Tracking("BR123", null, null), s_now);

        Assert.Equal(SupplierOrderChange.None, order.Fail(SupplierFailureReasons.SupplierCancelled, "x", s_now));
        Assert.Equal(SupplierOrderStatus.Shipped, order.Status);
    }

    [Fact]
    public void Entregue_sem_o_enviado_antes_completa_as_datas()
    {
        var order = NewOrder();

        Assert.Equal(SupplierOrderChange.Delivered, order.MarkDelivered(s_now));
        Assert.NotNull(order.PlacedAt);
        Assert.NotNull(order.ShippedAt);
    }

    [Fact]
    public void Erro_passageiro_espera_cada_vez_mais_e_desiste_no_limite()
    {
        var order = NewOrder();

        Assert.Equal(SupplierOrderChange.None, order.MarkAttemptFailed("timeout", s_now));
        Assert.Equal(SupplierOrderStatus.Pending, order.Status);
        var first = order.NextAttemptAt!.Value;
        order.MarkAttemptFailed("timeout", s_now);
        Assert.True(order.NextAttemptAt > first, "a espera cresce");

        var change = SupplierOrderChange.None;
        while (order.Attempts < SupplierOrder.MaxAttempts)
        {
            change = order.MarkAttemptFailed("timeout", s_now);
        }
        Assert.Equal(SupplierOrderChange.Failed, change);
        Assert.Equal(SupplierFailureReasons.SupplierUnavailable, order.FailureReason);
        Assert.True(order.NextAttemptAt <= s_now.AddMinutes(5), "a espera não passa de 5 minutos");
    }

    [Fact]
    public void Recusa_de_negocio_nao_tenta_de_novo()
    {
        var order = NewOrder();

        Assert.Equal(SupplierOrderChange.Failed, order.Fail(SupplierFailureReasons.OutOfStock, "Out of stock: MOU-000001", s_now));
        Assert.Equal(SupplierOrderChange.None, order.MarkAttemptFailed("x", s_now));
        Assert.Equal(SupplierOrderChange.None, order.MarkPlaced("CJ1", s_now));
        Assert.Equal(SupplierOrderStatus.Failed, order.Status);
    }

    [Fact]
    public void Pedido_sem_item_nao_existe() =>
        Assert.Throws<ArgumentException>(() => SupplierOrder.Create(Guid.NewGuid(), "a@x.com", [], "cj", s_now));
}
