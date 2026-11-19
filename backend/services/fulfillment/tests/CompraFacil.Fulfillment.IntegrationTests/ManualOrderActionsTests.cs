using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;
using CompraFacil.Fulfillment.Infrastructure;
using CompraFacil.Fulfillment.Infrastructure.Supplier;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Fulfillment.IntegrationTests;

/// <summary>A fila "Pedidos a fazer" e as ações da equipe no fornecedor manual (#412), pela API do painel.</summary>
public sealed class ManualOrderActionsTests(ManualSupplierFixture fx) : IClassFixture<ManualSupplierFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> PaidOrderAsync(params OrderItem[] items)
    {
        using (var upload = await ManualSupplierTests.UploadAsync(fx, ManualSupplierTests.Sheet, fx.Identity.AdminToken()))
        {
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        }
        var id = Guid.NewGuid();
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var handler = ActivatorUtilities.CreateInstance<OnOrderConfirmed>(scope.ServiceProvider);
            if (items.Length == 0)
            {
                items = [new OrderItem(Guid.NewGuid(), "FON-BT-01", "Fone Bluetooth", 1, new Money(15990, "BRL"), new Money(15990, "BRL"))];
            }
            await handler.HandleAsync(new OrderConfirmedData(id, Guid.NewGuid(), "ana@exemplo.com", items, new Money(17980, "BRL"),
                    new Recipient("Ana Souza", "52998224725", "11987654321"),
                    new ShippingAddress("01310100", "Avenida Paulista", "1000", null, "Bela Vista", "São Paulo", "SP"),
                    "pix", new OrderShipping("economico", new Money(1990, "BRL"), new DateOnly(2026, 12, 28))),
                new EventContext(Guid.NewGuid(), FulfillmentEvents.OrderConfirmed, FulfillmentEvents.OrdersTopic, "req", DateTimeOffset.UtcNow), Ct);
            await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().SaveChangesAsync(Ct);
        }
        await ActivatorUtilities.CreateInstance<SupplierDispatcher>(fx.Factory.Services).DispatchOnceAsync(Ct);
        return id;
    }

    private async Task<HttpResponseMessage> PostAsync(string path, object? body, string token)
    {
        using var client = fx.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return body is null ? await client.PostAsync(path, null, Ct) : await client.PostAsJsonAsync(path, body, Ct);
    }

    private async Task<List<string>> EventsAsync(Guid id)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var events = await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().Set<OutboxMessage>()
            .Where(m => m.MessageKey == id.ToString() && m.Topic == FulfillmentEvents.Topic).OrderBy(m => m.Id).ToListAsync(Ct);
        foreach (var e in events)
        {
            ContractAssert.AssertEvent("fulfillment.v1.yaml", e.EventType, e.Payload);
        }
        return [.. events.Select(e => e.EventType)];
    }

    private async Task<SupplierOrder> OrderAsync(Guid id)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().SupplierOrders.AsNoTracking().SingleAsync(o => o.Id == id, Ct);
    }

    [Fact]
    public async Task Pagamento_revertido_tira_o_pedido_da_fila_da_equipe()
    {
        // na fila "Pedidos a fazer": o orders cancelou (o cartão aprovado não se confirmou), e o pedido sai da fila
        var id = await PaidOrderAsync();
        Assert.Equal(SupplierOrderStatus.AwaitingStaff, (await OrderAsync(id)).Status);

        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var handler = ActivatorUtilities.CreateInstance<OnOrderCancelled>(scope.ServiceProvider);
            await handler.HandleAsync(new OrderCancelledData(id, "payment_failed"),
                new EventContext(Guid.NewGuid(), FulfillmentEvents.OrderCancelled, FulfillmentEvents.OrdersTopic, "req", DateTimeOffset.UtcNow), Ct);
            await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().SaveChangesAsync(Ct);
        }

        Assert.Equal((SupplierOrderStatus.Failed, SupplierFailureReasons.OrderCancelled), ((await OrderAsync(id)).Status, (await OrderAsync(id)).FailureReason));
        Assert.Equal([FulfillmentEvents.SupplierOrderFailed], await EventsAsync(id));
        using var admin = fx.Factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fx.Identity.AdminToken());
        var queue = await admin.GetStringAsync("/v1/admin/supplier-orders/to-place", Ct);
        Assert.DoesNotContain(id.ToString(), queue, StringComparison.Ordinal);
        var failed = await admin.GetStringAsync("/v1/admin/supplier-orders/failed?limit=200", Ct);
        ContractAssert.AssertSchema("fulfillment.v1.yaml", "/paths/~1v1~1admin~1supplier-orders~1failed/get/responses/200/content/application~1json/schema", failed);
        Assert.Contains(id.ToString(), failed, StringComparison.Ordinal);

        // a equipe não faz mais o pedido no fornecedor
        using var placed = await PostAsync($"/v1/admin/supplier-orders/{id}/placed", new { supplier_order_id = "F-9" }, fx.Identity.AdminToken());
        Assert.NotEqual(HttpStatusCode.OK, placed.StatusCode);
    }

    [Fact]
    public async Task O_tamanho_escolhido_chega_na_fila_um_item_por_tamanho()
    {
        var product = Guid.NewGuid();
        var id = await PaidOrderAsync(
            new OrderItem(product, "FON-BT-01", "Camiseta Natal", 1, new Money(7990, "BRL"), new Money(7990, "BRL"), "P"),
            new OrderItem(product, "FON-BT-01", "Camiseta Natal", 2, new Money(7990, "BRL"), new Money(15980, "BRL"), "G"));

        using var admin = fx.Factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fx.Identity.AdminToken());
        var json = await admin.GetStringAsync("/v1/admin/supplier-orders/to-place", Ct);
        ContractAssert.AssertSchema("fulfillment.v1.yaml",
            "/paths/~1v1~1admin~1supplier-orders~1to-place/get/responses/200/content/application~1json/schema", json);
        var mine = JsonDocument.Parse(json).RootElement.GetProperty("data").EnumerateArray().Single(o => o.GetProperty("order_id").GetGuid() == id);
        var items = mine.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("size").GetString(), i.GetProperty("quantity").GetInt32()));
        Assert.Equal([("P", 1), ("G", 2)], items);
        Assert.Equal(3 * 8990, mine.GetProperty("expected_product_cost_cents").GetInt64()); // o custo soma os dois tamanhos
    }

    [Fact]
    public async Task Da_fila_ao_entregue_pelo_painel_com_os_eventos_de_sempre()
    {
        var id = await PaidOrderAsync();

        // a fila: o operador vê a entrega do cliente; o auditor (o Balcão), só a idade e os valores
        using var admin = fx.Factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fx.Identity.AdminToken());
        var queue = (await admin.GetFromJsonAsync<JsonElement>("/v1/admin/supplier-orders/to-place", Ct)).GetProperty("data");
        var mine = queue.EnumerateArray().Single(o => o.GetProperty("order_id").GetGuid() == id);
        Assert.Equal("52998224725", mine.GetProperty("ship_to").GetProperty("cpf").GetString());
        Assert.Equal(8990, mine.GetProperty("expected_product_cost_cents").GetInt64());
        Assert.Equal("FON-BT-01", mine.GetProperty("items")[0].GetProperty("sku").GetString());

        using var auditor = fx.Factory.CreateClient();
        auditor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fx.Identity.AuditorToken());
        var seen = (await auditor.GetFromJsonAsync<JsonElement>("/v1/admin/supplier-orders/to-place", Ct)).GetProperty("data")
            .EnumerateArray().Single(o => o.GetProperty("order_id").GetGuid() == id);
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("ship_to").ValueKind);
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("customer_email").ValueKind);

        // o rastreio antes de o pedido ser feito não vale; o auditor não age; sem o número, 422
        using (var early = await PostAsync($"/v1/admin/supplier-orders/{id}/tracking", new { number = "BR123" }, fx.Identity.AdminToken()))
        {
            Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        }
        using (var forbidden = await PostAsync($"/v1/admin/supplier-orders/{id}/placed", new { supplier_order_id = "F-1" }, fx.Identity.AuditorToken()))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        using (var invalid = await PostAsync($"/v1/admin/supplier-orders/{id}/placed", new { supplier_order_id = " " }, fx.Identity.AdminToken()))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        }

        // feito no fornecedor, com o que a equipe pagou: a margem passa a usar o pago
        using (var placed = await PostAsync($"/v1/admin/supplier-orders/{id}/placed",
            new { supplier_order_id = "PED-7781", paid_product_cost_cents = 9500, paid_freight_cost_cents = 2200 }, fx.Identity.AdminToken()))
        {
            Assert.Equal(HttpStatusCode.NoContent, placed.StatusCode);
        }
        var order = await OrderAsync(id);
        Assert.Equal((SupplierOrderStatus.Placed, "PED-7781", 9500L, 2200L), (order.Status, order.SupplierOrderId, order.PaidProductCostCents!.Value, order.PaidFreightCostCents!.Value));
        Assert.NotNull(order.PlacedBy);
        // feito, sai da fila e aparece entre os que esperam o rastreio
        Assert.DoesNotContain((await admin.GetFromJsonAsync<JsonElement>("/v1/admin/supplier-orders/to-place", Ct)).GetProperty("data").EnumerateArray(),
            o => o.GetProperty("order_id").GetGuid() == id);
        var toShip = (await admin.GetFromJsonAsync<JsonElement>("/v1/admin/supplier-orders/manual?status=placed", Ct)).GetProperty("data")
            .EnumerateArray().Single(o => o.GetProperty("order_id").GetGuid() == id);
        Assert.Equal(("placed", "PED-7781"), (toShip.GetProperty("status").GetString(), toShip.GetProperty("supplier_order_id").GetString()));
        using (var badStage = await admin.GetAsync("/v1/admin/supplier-orders/manual?status=failed", Ct))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, badStage.StatusCode);
        }
        var margins = await admin.GetFromJsonAsync<JsonElement>("/v1/admin/order-margins", Ct);
        var margin = margins.GetProperty("data").EnumerateArray().Single(m => m.GetProperty("order_id").GetGuid() == id);
        Assert.Equal((9500, 2200), (margin.GetProperty("product_cost_cents").GetInt64(), margin.GetProperty("freight_cents").GetInt64()));

        using (var tracking = await PostAsync($"/v1/admin/supplier-orders/{id}/tracking",
            new { number = "BR123456789BR", carrier = "Correios", url = "https://rastreamento.correios.com.br/" }, fx.Identity.AdminToken()))
        {
            Assert.Equal(HttpStatusCode.NoContent, tracking.StatusCode);
        }
        var inTransit = (await admin.GetFromJsonAsync<JsonElement>("/v1/admin/supplier-orders/manual?status=shipped", Ct)).GetProperty("data")
            .EnumerateArray().Single(o => o.GetProperty("order_id").GetGuid() == id);
        Assert.Equal("BR123456789BR", inTransit.GetProperty("tracking").GetProperty("number").GetString());
        using (var delivered = await PostAsync($"/v1/admin/supplier-orders/{id}/delivered", null, fx.Identity.AdminToken()))
        {
            Assert.Equal(HttpStatusCode.NoContent, delivered.StatusCode);
        }
        Assert.Equal([FulfillmentEvents.SupplierOrderPlaced, FulfillmentEvents.SupplierOrderShipped, FulfillmentEvents.SupplierOrderDelivered], await EventsAsync(id));
        Assert.Equal("BR123456789BR", (await OrderAsync(id)).Tracking!.Number);
    }

    [Fact]
    public async Task Sem_estoque_estorna_e_o_feito_no_fornecedor_so_cancela_com_a_confirmacao()
    {
        var missing = await PaidOrderAsync();
        using (var outOfStock = await PostAsync($"/v1/admin/supplier-orders/{missing}/out-of-stock", null, fx.Identity.AdminToken()))
        {
            Assert.Equal(HttpStatusCode.NoContent, outOfStock.StatusCode);
        }
        var failed = await OrderAsync(missing);
        Assert.Equal((SupplierOrderStatus.Failed, SupplierFailureReasons.OutOfStock), (failed.Status, failed.FailureReason));
        Assert.Equal([FulfillmentEvents.SupplierOrderFailed], await EventsAsync(missing));

        var placed = await PaidOrderAsync();
        using (var ok = await PostAsync($"/v1/admin/supplier-orders/{placed}/placed", new { supplier_order_id = "PED-1" }, fx.Identity.AdminToken()))
        {
            Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        }
        using (var unconfirmed = await PostAsync($"/v1/admin/supplier-orders/{placed}/cancel", null, fx.Identity.AdminToken()))
        {
            Assert.Equal(HttpStatusCode.Conflict, unconfirmed.StatusCode);
            Assert.Contains("cancelled_at_supplier", await unconfirmed.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
        Assert.Equal(SupplierOrderStatus.Placed, (await OrderAsync(placed)).Status);
        using (var confirmed = await PostAsync($"/v1/admin/supplier-orders/{placed}/cancel", new { cancelled_at_supplier = true }, fx.Identity.AdminToken()))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        }
        var cancelled = await OrderAsync(placed);
        Assert.Equal((SupplierOrderStatus.Failed, SupplierFailureReasons.StoreCancelled), (cancelled.Status, cancelled.FailureReason));
    }
}
