using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Fulfillment.Application.Invoices;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;
using CompraFacil.Fulfillment.Infrastructure;
using CompraFacil.Fulfillment.Infrastructure.Supplier;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompraFacil.Fulfillment.IntegrationTests;

/// <summary>O fornecedor dos testes: responde o que o teste mandar, e conta as chamadas.</summary>
public sealed class FakeSupplier : ISupplierGateway
{
    public Func<SupplierOrderRequest, SupplierPlacement> Respond { get; set; } = r => new SupplierPlacement.Accepted("CJ" + r.OrderNumber.ToString("N")[..10]);
    public List<SupplierOrderRequest> Calls { get; } = [];
    public string Name => "fake";

    /// <summary>O feed que o fornecedor devolve; nulo = fora do ar.</summary>
    public List<SupplierFeedItem>? Feed { get; set; } = [];

    public Task<SupplierPlacement> PlaceOrderAsync(SupplierOrderRequest request, CancellationToken ct)
    {
        Calls.Add(request);
        return Task.FromResult(Respond(request));
    }

    /// <summary>As opções de frete que o fornecedor cota; nulo = fora do ar.</summary>
    public List<SupplierFreightOption>? Freight { get; set; } =
        [new("CJ Express", 1200, 6, 10), new("CJPacket Ordinary", 450, 12, 20), new("Serviço desconhecido", 100, 30, 40)];

    public List<(string Zip, IReadOnlyList<SupplierOrderItem> Items)> FreightCalls { get; } = [];

    public Task<IReadOnlyList<SupplierFreightOption>> QuoteFreightAsync(string zipCode, IReadOnlyList<SupplierOrderItem> items, CancellationToken ct)
    {
        FreightCalls.Add((zipCode, items));
        return Freight is { } freight
            ? Task.FromResult<IReadOnlyList<SupplierFreightOption>>([.. freight])
            : throw new SupplierUnavailableException("frete fora do ar");
    }

    /// <summary>O estoque por vid que o fornecedor responde (vid ausente: 0); nulo = fora do ar.</summary>
    public Dictionary<string, int>? Stock { get; set; } = [];

    public Task<IReadOnlyDictionary<string, int>> GetStockAsync(IReadOnlyCollection<string> vids, CancellationToken ct) =>
        Stock is { } stock
            ? Task.FromResult<IReadOnlyDictionary<string, int>>(vids.ToDictionary(v => v, v => stock.GetValueOrDefault(v)))
            : throw new SupplierUnavailableException("estoque fora do ar");

    public Task<IReadOnlyList<SupplierFeedItem>> ListProductsAsync(CancellationToken ct) =>
        Feed is { } feed
            ? Task.FromResult<IReadOnlyList<SupplierFeedItem>>([.. feed])
            : throw new SupplierUnavailableException("fornecedor fora do ar");
}

/// <summary>O emissor de nota dos testes: autoriza por padrão, ou responde o que o teste mandar.</summary>
public sealed class FakeInvoiceIssuer : IInvoiceIssuer
{
    private int _number = 1000;
    public Func<Invoice, InvoiceIssue>? Respond { get; set; }
    public List<Invoice> Issued { get; } = [];
    public List<string> Cancelled { get; } = [];

    /// <summary>O que o cancelamento faz; nulo = cancela.</summary>
    public Func<string, Exception?> CancelError { get; set; } = _ => null;

    public Task<InvoiceIssue> IssueAsync(Invoice invoice, CancellationToken ct)
    {
        Issued.Add(invoice);
        return Task.FromResult(Respond?.Invoke(invoice)
            ?? new InvoiceIssue.Authorized("nfe_" + invoice.Id.ToString("N")[..12], "3" + new string('1', 43), Interlocked.Increment(ref _number), 1, DateTimeOffset.UtcNow));
    }

    /// <summary>O que a nota de devolução responde; nulo = autoriza.</summary>
    public Func<Invoice, InvoiceIssue>? RespondReturn { get; set; }
    public List<Invoice> ReturnsIssued { get; } = [];

    public Task<InvoiceIssue> IssueReturnAsync(Invoice invoice, CancellationToken ct)
    {
        ReturnsIssued.Add(invoice);
        return Task.FromResult(RespondReturn?.Invoke(invoice)
            ?? new InvoiceIssue.Authorized("nfe_dev_" + invoice.Id.ToString("N")[..8], "4" + new string('2', 43), Interlocked.Increment(ref _number), 1, DateTimeOffset.UtcNow));
    }

    public Task<string> GetXmlAsync(string providerId, CancellationToken ct) => Task.FromResult($"<nfeProc><id>{providerId}</id></nfeProc>");

    public Task<byte[]> GetPdfAsync(string providerId, CancellationToken ct) => Task.FromResult("%PDF-1.4"u8.ToArray());

    public Task CancelAsync(string providerId, string justification, CancellationToken ct)
    {
        if (CancelError(providerId) is { } e)
        {
            throw e;
        }
        Cancelled.Add(providerId);
        return Task.CompletedTask;
    }
}

public sealed class FulfillmentFixture : IAsyncLifetime
{
    public const string Secret = "segredo-dos-testes";
    private readonly PostgresFixture _postgres = new();
    public TestIdentity Identity { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public FakeSupplier Supplier { get; } = new();
    public FakeInvoiceIssuer Issuer { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _postgres.ConnectionString)
            .UseSetting("Kafka:Enabled", "false")
            .UseSetting("Supplier:DispatcherEnabled", "false")
            .UseSetting("Supplier:CatalogSyncEnabled", "false")
            .UseSetting("Invoice:ReturnEnabled", "false")
            .UseSetting("Invoice:CancellerEnabled", "false")
            .UseSetting("Supplier:WebhookSecret", Secret)
            .UseSetting("Auth:Jwks", Identity.Jwks)
            .ConfigureTestServices(s =>
            {
                s.RemoveAll<ISupplierGateway>();
                s.AddSingleton<ISupplierGateway>(Supplier);
                s.RemoveAll<IInvoiceIssuer>();
                s.AddSingleton<IInvoiceIssuer>(Issuer);
            }));
        using var _ = Factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
        Identity.Dispose();
    }
}

/// <summary>O pedido pago até o fornecedor e de volta: fila, despachante, webhook e os eventos.</summary>
public sealed class FulfillmentTests(FulfillmentFixture fx) : IClassFixture<FulfillmentFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task ConfirmedAsync(
        Guid orderId, string email = "ana@exemplo.com", DateTimeOffset? confirmedAt = null, OrderCutoff? cutoff = null, Guid? customer = null)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var handler = cutoff is null
            ? ActivatorUtilities.CreateInstance<OnOrderConfirmed>(scope.ServiceProvider)
            : ActivatorUtilities.CreateInstance<OnOrderConfirmed>(scope.ServiceProvider, cutoff);
        var item = new OrderItem(Guid.NewGuid(), "MOU-000001", "Mouse sem Fio", 2, new Money(9990, "BRL"), new Money(19980, "BRL"));
        await handler.HandleAsync(new OrderConfirmedData(orderId, customer ?? Guid.NewGuid(), email, [item], new Money(19980, "BRL"),
                new Recipient("Ana Souza", "52998224725", "11987654321"),
                new ShippingAddress("01310100", "Avenida Paulista", "1000", "Apto 12", "Bela Vista", "São Paulo", "SP"),
                "card", new OrderShipping("economico", new Money(2430, "BRL"), new DateOnly(2026, 12, 28))),
            new EventContext(Guid.NewGuid(), FulfillmentEvents.OrderConfirmed, FulfillmentEvents.OrdersTopic, "req", confirmedAt ?? DateTimeOffset.UtcNow), Ct);
        await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().SaveChangesAsync(Ct);
    }

    private async Task<Invoice?> InvoiceAsync(Guid id)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, Ct);
    }

    private async Task ReturnedAsync(Guid id)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<CompraFacil.Fulfillment.Application.Invoices.OnOrderReturned>(scope.ServiceProvider);
        await handler.HandleAsync(new OrderReturnedData(id),
            new EventContext(Guid.NewGuid(), InvoiceEvents.OrderReturned, FulfillmentEvents.OrdersTopic, "req", DateTimeOffset.UtcNow), Ct);
        await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().SaveChangesAsync(Ct);
    }

    private async Task<int> IssueReturnInvoicesAsync() =>
        await ActivatorUtilities.CreateInstance<CompraFacil.Fulfillment.Infrastructure.Invoices.ReturnInvoiceJob>(fx.Factory.Services).IssueOnceAsync(Ct);

    private async Task RefundedAsync(Guid id, bool full = true, string? reason = null)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<OnPaymentRefunded>(scope.ServiceProvider);
        await handler.HandleAsync(new PaymentRefundedData(id, full, reason),
            new EventContext(Guid.NewGuid(), InvoiceEvents.PaymentRefunded, InvoiceEvents.PaymentsTopic, "req", DateTimeOffset.UtcNow), Ct);
        await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().SaveChangesAsync(Ct);
    }

    private async Task<int> CancelInvoicesAsync() =>
        await ActivatorUtilities.CreateInstance<CompraFacil.Fulfillment.Infrastructure.Invoices.InvoiceCancellerJob>(fx.Factory.Services).CancelOnceAsync(Ct);

    private async Task<int> DispatchAsync()
    {
        var dispatcher = ActivatorUtilities.CreateInstance<SupplierDispatcher>(fx.Factory.Services);
        return await dispatcher.DispatchOnceAsync(Ct);
    }

    private async Task<SupplierOrder> OrderAsync(Guid id)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().SupplierOrders.AsNoTracking().SingleAsync(o => o.Id == id, Ct);
    }

    /// <summary>Os eventos do pedido num tópico (o do fornecedor, por padrão), na ordem, cada um conferido contra o contrato.</summary>
    private async Task<List<string>> EventsOfAsync(Guid id, string topic = FulfillmentEvents.Topic)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var events = await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().Set<OutboxMessage>()
            .Where(m => m.MessageKey == id.ToString() && m.Topic == topic).OrderBy(m => m.Id).ToListAsync(Ct);
        foreach (var e in events)
        {
            ContractAssert.AssertEvent("fulfillment.v1.yaml", e.EventType, e.Payload);
        }
        return [.. events.Select(e => e.EventType)];
    }

    private async Task<(HttpStatusCode Status, string Body)> WebhookAsync(object body, string? secret = FulfillmentFixture.Secret)
    {
        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        if (secret is not null)
        {
            var hmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(json));
            content.Headers.Add("X-Sim-Signature", "sha256=" + Convert.ToHexStringLower(hmac));
        }
        using var client = fx.Factory.CreateClient();
        using var response = await client.PostAsync("/v1/webhooks/supplier", content, Ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    private static object Update(Guid orderId, string status, string? track = null, string? messageId = null) => new
    {
        messageId = messageId ?? Guid.NewGuid().ToString(),
        type = "ORDER",
        messageType = "UPDATE",
        @params = new
        {
            orderId = "CJ0000000001",
            orderNumber = orderId.ToString(),
            orderStatus = status,
            trackNumber = track,
            trackingProvider = track is null ? null : "Correios",
            trackingUrl = track is null ? null : $"https://t.17track.net/en#nums={track}",
            reason = (string?)null,
        },
    };

    [Fact]
    public async Task Pedido_pago_vai_ao_fornecedor_uma_vez_e_volta_entregue()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);
        await ConfirmedAsync(id); // o mesmo evento de novo não cria outro pedido
        Assert.Equal(SupplierOrderStatus.Pending, (await OrderAsync(id)).Status);

        await DispatchAsync();
        var placed = await OrderAsync(id);
        Assert.Equal(SupplierOrderStatus.Placed, placed.Status);
        var call = Assert.Single(fx.Supplier.Calls, c => c.OrderNumber == id);
        Assert.Equal("MOU-000001", Assert.Single(call.Items).Sku);
        // a nota foi emitida antes e vai com o pedido (#221), com o frete (#222)
        var invoice = (await InvoiceAsync(id))!;
        Assert.Equal((2430L, 19980L + 2430, "card"), (invoice.FreightCents, invoice.TotalCents, invoice.PaymentMethod));
        Assert.Equal(new DateOnly(2026, 12, 28), placed.PromisedBy);
        Assert.Equal(InvoiceStatus.Authorized, invoice.Status);
        Assert.Equal((invoice.AccessKey, $"<nfeProc><id>{invoice.ProviderId}</id></nfeProc>"), (call.InvoiceKey, call.InvoiceXml));
        Assert.Equal([InvoiceEvents.InvoiceIssued], await EventsOfAsync(id, InvoiceEvents.Topic));
        // para quem e para onde, guardado com o pedido (#221): vale também para as novas tentativas
        Assert.Equal(new ShipTo("Ana Souza", "52998224725", "11987654321", "01310100", "Avenida Paulista", "1000", "Apto 12",
            "Bela Vista", "São Paulo", "SP"), call.ShipTo);
        Assert.Equal(call.ShipTo, placed.ShipTo);

        Assert.Equal(HttpStatusCode.OK, (await WebhookAsync(Update(id, "UNSHIPPED"))).Status);
        var shipped = Update(id, "SHIPPED", "BR123456789CJ", messageId: "msg-enviado-" + id);
        Assert.Contains("applied", (await WebhookAsync(shipped)).Body);
        Assert.Contains("duplicate", (await WebhookAsync(shipped)).Body); // o fornecedor repete
        Assert.Contains("applied", (await WebhookAsync(Update(id, "DELIVERED"))).Body);
        // atrasado: o aceite chega depois da entrega e não faz voltar
        Assert.Contains("ignored", (await WebhookAsync(Update(id, "UNSHIPPED"))).Body);

        var delivered = await OrderAsync(id);
        Assert.Equal(SupplierOrderStatus.Delivered, delivered.Status);
        Assert.Equal("BR123456789CJ", delivered.Tracking!.Number);
        Assert.Equal(
            [FulfillmentEvents.SupplierOrderPlaced, FulfillmentEvents.SupplierOrderShipped, FulfillmentEvents.SupplierOrderDelivered],
            await EventsOfAsync(id));
    }

    [Fact]
    public async Task Pedido_confirmado_antes_do_corte_nao_vai_ao_fornecedor()
    {
        var cutoff = new OrderCutoff(DateTimeOffset.UtcNow);
        var old = Guid.NewGuid();
        var fresh = Guid.NewGuid();

        await ConfirmedAsync(old, confirmedAt: cutoff.Since!.Value.AddDays(-2), cutoff: cutoff);
        await ConfirmedAsync(fresh, confirmedAt: cutoff.Since.Value.AddSeconds(1), cutoff: cutoff);

        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>();
        Assert.False(await db.SupplierOrders.AnyAsync(o => o.Id == old, Ct));
        Assert.True(await db.SupplierOrders.AnyAsync(o => o.Id == fresh, Ct));
    }

    [Fact]
    public async Task Recusa_do_fornecedor_vira_falha_sem_nova_tentativa()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);
        fx.Supplier.Respond = r => r.OrderNumber == id
            ? new SupplierPlacement.Rejected(SupplierFailureReasons.OutOfStock, "Out of stock: MOU-000001")
            : new SupplierPlacement.Accepted("CJ1");

        await DispatchAsync();
        await DispatchAsync();

        var order = await OrderAsync(id);
        Assert.Equal(SupplierOrderStatus.Failed, order.Status);
        Assert.Equal(SupplierFailureReasons.OutOfStock, order.FailureReason);
        Assert.Single(fx.Supplier.Calls, c => c.OrderNumber == id);
        Assert.Equal([FulfillmentEvents.SupplierOrderFailed], await EventsOfAsync(id));
    }

    [Fact]
    public async Task Fornecedor_fora_do_ar_volta_para_a_fila_e_espera()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);
        fx.Supplier.Respond = r => r.OrderNumber == id
            ? throw new SupplierUnavailableException("fornecedor respondeu 503")
            : new SupplierPlacement.Accepted("CJ1");

        await DispatchAsync();
        await DispatchAsync(); // ainda na espera: não tenta de novo

        var order = await OrderAsync(id);
        Assert.Equal(SupplierOrderStatus.Pending, order.Status);
        Assert.Equal(1, order.Attempts);
        Assert.NotNull(order.NextAttemptAt);
        Assert.Single(fx.Supplier.Calls, c => c.OrderNumber == id);
        Assert.Empty(await EventsOfAsync(id));
    }

    [Fact]
    public async Task Cancelado_pelo_fornecedor_depois_de_aceito_vira_falha()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);
        await DispatchAsync();

        await WebhookAsync(Update(id, "CANCELLED"));

        var order = await OrderAsync(id);
        Assert.Equal(SupplierOrderStatus.Failed, order.Status);
        Assert.Equal(SupplierFailureReasons.SupplierCancelled, order.FailureReason);
        Assert.Equal([FulfillmentEvents.SupplierOrderPlaced, FulfillmentEvents.SupplierOrderFailed], await EventsOfAsync(id));
    }

    [Fact]
    public async Task Webhook_sem_a_assinatura_certa_e_recusado()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);

        Assert.Equal(HttpStatusCode.Unauthorized, (await WebhookAsync(Update(id, "SHIPPED", "BR1"), secret: "outro")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WebhookAsync(Update(id, "SHIPPED", "BR1"), secret: null)).Status);
        Assert.Equal(SupplierOrderStatus.Pending, (await OrderAsync(id)).Status);
    }

    [Fact]
    public async Task Webhook_de_pedido_desconhecido_responde_200()
    {
        var (status, body) = await WebhookAsync(Update(Guid.NewGuid(), "SHIPPED", "BR1"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("unknown_order", body);
        ContractAssert.AssertSchema("fulfillment.v1.yaml", "/components/schemas/SupplierWebhookAck", body);
    }

    [Fact]
    public async Task Nota_rejeitada_nao_vai_ao_fornecedor_e_falha_o_pedido()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);
        fx.Issuer.Respond = i => i.Id == id ? new InvoiceIssue.Rejected("nfe_x", 237, "Rejeição: CPF do destinatário inválido") : null!;
        try
        {
            await DispatchAsync();
        }
        finally
        {
            fx.Issuer.Respond = null;
        }

        var order = await OrderAsync(id);
        Assert.Equal((SupplierOrderStatus.Failed, SupplierFailureReasons.InvoiceRejected), (order.Status, order.FailureReason));
        Assert.Contains("237", order.FailureDetail, StringComparison.Ordinal);
        Assert.DoesNotContain(fx.Supplier.Calls, c => c.OrderNumber == id);
        Assert.Equal([FulfillmentEvents.SupplierOrderFailed], await EventsOfAsync(id));
        Assert.Equal([InvoiceEvents.InvoiceRejected], await EventsOfAsync(id, InvoiceEvents.Topic));
        Assert.Equal((InvoiceStatus.Rejected, 237), ((await InvoiceAsync(id))!.Status, (await InvoiceAsync(id))!.RejectionCode));
    }

    [Fact]
    public async Task Emissor_fora_do_ar_volta_para_a_fila_sem_emitir_duas_vezes()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);
        fx.Issuer.Respond = i => i.Id == id ? throw new InvoiceIssuerUnavailableException("emissor respondeu 503") : null!;
        try
        {
            await DispatchAsync();
        }
        finally
        {
            fx.Issuer.Respond = null;
        }

        var order = await OrderAsync(id);
        Assert.Equal((SupplierOrderStatus.Pending, 1), (order.Status, order.Attempts));
        Assert.Contains("emissor de nota", order.LastError, StringComparison.Ordinal);
        Assert.Equal(InvoiceStatus.Pending, (await InvoiceAsync(id))!.Status);
        Assert.DoesNotContain(fx.Supplier.Calls, c => c.OrderNumber == id);
        Assert.Empty(await EventsOfAsync(id, InvoiceEvents.Topic));
    }

    [Fact]
    public async Task Estorno_total_cancela_a_nota_e_o_parcial_nao()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);
        await DispatchAsync();
        var invoice = (await InvoiceAsync(id))!;

        await RefundedAsync(id, full: false); // estorno de parte, pelo painel: a nota continua
        await CancelInvoicesAsync();
        Assert.Equal(InvoiceStatus.Authorized, (await InvoiceAsync(id))!.Status);

        await RefundedAsync(id);
        await RefundedAsync(id); // repetido
        await CancelInvoicesAsync();
        await CancelInvoicesAsync();

        Assert.Equal(InvoiceStatus.Cancelled, (await InvoiceAsync(id))!.Status);
        Assert.Single(fx.Issuer.Cancelled, p => p == invoice.ProviderId);
        Assert.Equal([InvoiceEvents.InvoiceIssued, InvoiceEvents.InvoiceCancelled], await EventsOfAsync(id, InvoiceEvents.Topic));
    }

    [Fact]
    public async Task Desistencia_nao_cancela_a_nota_e_emite_a_de_devolucao()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);
        await DispatchAsync();
        var provider = (await InvoiceAsync(id))!.ProviderId;

        // o estorno da desistência chega antes ou depois do OrderReturned: nenhum dos dois cancela
        await RefundedAsync(id, reason: InvoiceEvents.ReturnRefund);
        await ReturnedAsync(id);
        await ReturnedAsync(id); // repetido
        await CancelInvoicesAsync();
        await IssueReturnInvoicesAsync();
        await IssueReturnInvoicesAsync();

        var invoice = (await InvoiceAsync(id))!;
        Assert.Equal(InvoiceStatus.Authorized, invoice.Status);
        Assert.DoesNotContain(provider, fx.Issuer.Cancelled);
        Assert.Single(fx.Issuer.ReturnsIssued, i => i.Id == id);
        Assert.NotNull(invoice.ReturnAccessKey);
        Assert.Equal([InvoiceEvents.InvoiceIssued, InvoiceEvents.InvoiceReturnIssued], await EventsOfAsync(id, InvoiceEvents.Topic));
    }

    [Fact]
    public async Task Nota_de_devolucao_rejeitada_fica_para_a_equipe()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);
        await DispatchAsync();
        fx.Issuer.RespondReturn = i => i.Id == id ? new InvoiceIssue.Rejected("nfe_x", 267, "sem referência") : null!;
        try
        {
            await ReturnedAsync(id);
            await IssueReturnInvoicesAsync();
            await IssueReturnInvoicesAsync(); // não insiste
        }
        finally
        {
            fx.Issuer.RespondReturn = null;
        }

        var invoice = (await InvoiceAsync(id))!;
        Assert.Equal("267: sem referência", invoice.ReturnError);
        Assert.Null(invoice.ReturnAccessKey);
        Assert.Single(fx.Issuer.ReturnsIssued, i => i.Id == id);
        Assert.Equal([InvoiceEvents.InvoiceIssued], await EventsOfAsync(id, InvoiceEvents.Topic));
    }

    [Fact]
    public async Task Cancelamento_recusado_pelo_emissor_fica_para_a_equipe()
    {
        var id = Guid.NewGuid();
        await ConfirmedAsync(id);
        await DispatchAsync();
        var provider = (await InvoiceAsync(id))!.ProviderId;
        fx.Issuer.CancelError = p => p == provider ? new InvoiceCancelRefusedException("prazo de cancelamento vencido") : null;
        try
        {
            await RefundedAsync(id);
            await CancelInvoicesAsync();
            await CancelInvoicesAsync(); // não insiste
        }
        finally
        {
            fx.Issuer.CancelError = _ => null;
        }

        var invoice = (await InvoiceAsync(id))!;
        Assert.Equal(InvoiceStatus.Authorized, invoice.Status);
        Assert.Contains("prazo", invoice.CancelError, StringComparison.Ordinal);
        Assert.Equal([InvoiceEvents.InvoiceIssued], await EventsOfAsync(id, InvoiceEvents.Topic));
    }

    [Fact]
    public async Task Pedido_sem_endereco_vai_sem_nota()
    {
        var id = Guid.NewGuid();
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var handler = ActivatorUtilities.CreateInstance<OnOrderConfirmed>(scope.ServiceProvider);
            var item = new OrderItem(Guid.NewGuid(), "MOU-000001", "Mouse", 1, new Money(9990, "BRL"), new Money(9990, "BRL"));
            await handler.HandleAsync(new OrderConfirmedData(id, Guid.NewGuid(), "ana@exemplo.com", [item], new Money(9990, "BRL")),
                new EventContext(Guid.NewGuid(), FulfillmentEvents.OrderConfirmed, FulfillmentEvents.OrdersTopic, "req", DateTimeOffset.UtcNow), Ct);
            await scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>().SaveChangesAsync(Ct);
        }
        Assert.Null(await InvoiceAsync(id));
    }

    [Fact]
    public async Task DANFE_so_para_o_dono_do_pedido_ou_a_equipe()
    {
        var (id, owner) = (Guid.NewGuid(), Guid.NewGuid());
        await ConfirmedAsync(id, customer: owner);
        using var client = fx.Factory.CreateClient();
        async Task<HttpResponseMessage> Get(Guid order, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/me/invoices/{order}/danfe");
            request.Headers.Authorization = new("Bearer", token);
            return await client.SendAsync(request, Ct);
        }

        using (var pending = await Get(id, fx.Identity.CustomerToken(owner)))
        {
            Assert.Equal(HttpStatusCode.NotFound, pending.StatusCode); // antes de emitir, não há nota
        }
        await DispatchAsync();

        using var mine = await Get(id, fx.Identity.CustomerToken(owner));
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal("application/pdf", mine.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("nfe-", mine.Content.Headers.ContentDisposition?.FileName, StringComparison.Ordinal);
        Assert.StartsWith("%PDF", await mine.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        using var other = await Get(id, fx.Identity.CustomerToken(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        using var admin = await Get(id, fx.Identity.AdminToken());
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        using var anonymous = await client.GetAsync($"/v1/me/invoices/{id}/danfe", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task DANFE_da_devolucao_so_depois_de_emitida_e_para_o_dono()
    {
        var (id, owner) = (Guid.NewGuid(), Guid.NewGuid());
        await ConfirmedAsync(id, customer: owner);
        await DispatchAsync();
        using var client = fx.Factory.CreateClient();
        async Task<HttpStatusCode> StatusFor(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/me/invoices/{id}/return-danfe");
            request.Headers.Authorization = new("Bearer", token);
            using var response = await client.SendAsync(request, Ct);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.NotFound, await StatusFor(fx.Identity.CustomerToken(owner)));
        await ReturnedAsync(id);
        await IssueReturnInvoicesAsync();

        Assert.Equal(HttpStatusCode.OK, await StatusFor(fx.Identity.CustomerToken(owner)));
        Assert.Equal(HttpStatusCode.OK, await StatusFor(fx.Identity.AdminToken()));
        Assert.Equal(HttpStatusCode.NotFound, await StatusFor(fx.Identity.CustomerToken(Guid.NewGuid())));
    }
}
