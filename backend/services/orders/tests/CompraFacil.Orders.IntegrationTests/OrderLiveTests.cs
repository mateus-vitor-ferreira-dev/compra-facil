using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Orders.Application.Orders;
using CompraFacil.Orders.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Orders.IntegrationTests;

/// <summary>Um evento SSE lido do stream: <c>id</c>, <c>event</c> e <c>data</c>; Comment para "": keep-alive".</summary>
public sealed record SseEvent(string? Id, string? Event, string? Data, string? Comment);

/// <summary>Cliente do stream: cadastra o pedido e lê os eventos como o EventSource faria.</summary>
public sealed class LiveClient(OrdersFixture fx)
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HttpClient Http { get; } = fx.Factory.CreateClient();

    public async Task<(Guid Order, Guid Customer)> PlaceAsync()
    {
        var product = fx.Catalog.Add(new CatalogProduct("SKU-LIVE", "Produto", 2500));
        var customer = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/orders")
        {
            Content = JsonContent.Create(new { items = new[] { new { product_id = product, quantity = 1 } } }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Authorization = new("Bearer", fx.Identity.CustomerToken(customer));
        using var response = await Http.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid(), customer);
    }

    public async Task<HttpResponseMessage> OpenAsync(Guid order, string? token, string? lastEventId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/orders/{order}/events");
        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }
        if (lastEventId is not null)
        {
            request.Headers.Add("Last-Event-ID", lastEventId);
        }
        return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
    }

    public async Task ConfirmAsync(Guid order)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<OnPaymentApproved>(scope.ServiceProvider);
        await handler.HandleAsync(new PaymentApprovedData(order, Guid.NewGuid()),
            new EventContext(Guid.NewGuid(), "PaymentApproved", "t", "c", DateTimeOffset.UtcNow), Ct);
        await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().SaveChangesAsync(Ct);
    }

    /// <summary>O fornecedor entregou: o estado final do pedido pago.</summary>
    public async Task DeliverAsync(Guid order)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<OnSupplierOrderDelivered>(scope.ServiceProvider);
        await handler.HandleAsync(new SupplierOrderDeliveredData(order),
            new EventContext(Guid.NewGuid(), "SupplierOrderDelivered", "t", "c", DateTimeOffset.UtcNow), Ct);
        await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().SaveChangesAsync(Ct);
    }

    /// <summary>StockReserved como o consumer de eventos aplica: dentro de uma transação, com commit no fim.</summary>
    public async Task ReserveAsync(Guid order)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(Ct);
        var handler = ActivatorUtilities.CreateInstance<OnStockReserved>(scope.ServiceProvider);
        await handler.HandleAsync(new StockReservedData(order),
            new EventContext(Guid.NewGuid(), "StockReserved", "t", "c", DateTimeOffset.UtcNow), Ct);
        await db.SaveChangesAsync(Ct);
        await tx.CommitAsync(Ct);
    }

    /// <summary>Lê eventos até o servidor fechar ou dar o prazo; para em <paramref name="until"/>, se vier.</summary>
    public static async Task<List<SseEvent>> ReadAsync(HttpResponseMessage response, TimeSpan timeout, Func<List<SseEvent>, bool>? until = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(timeout);
        var events = new List<SseEvent>();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));
        string? id = null, type = null, data = null;
        try
        {
            while (await reader.ReadLineAsync(cts.Token) is { } line)
            {
                if (line.Length == 0)
                {
                    if (type is not null)
                    {
                        events.Add(new SseEvent(id, type, data, null));
                    }
                    (id, type, data) = (null, null, null);
                    if (until?.Invoke(events) == true)
                    {
                        break;
                    }
                    continue;
                }
                if (line.StartsWith(':'))
                {
                    events.Add(new SseEvent(null, null, null, line[1..].Trim()));
                }
                else if (line.StartsWith("id: ", StringComparison.Ordinal))
                {
                    id = line[4..];
                }
                else if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    type = line[7..];
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    data = line[6..];
                }
            }
        }
        catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
        {
            // prazo do teste
        }
        return events;
    }

    public static string Status(SseEvent e) => JsonDocument.Parse(e.Data!).RootElement.GetProperty("status").GetString()!;
}

/// <summary>GET /v1/orders/{id}/events: o estado atual, cada mudança e o fim no estado final.</summary>
public sealed class OrderLiveTests(OrdersFixture fx) : IClassFixture<OrdersFixture>
{
    private readonly LiveClient _live = new(fx);

    private static List<SseEvent> Orders(List<SseEvent> events) => [.. events.Where(e => e.Event == "order")];

    [Fact]
    public async Task Stream_SendsTheCurrentState_ThenEachChange_AndClosesWhenDelivered()
    {
        var (order, customer) = await _live.PlaceAsync();
        using var response = await _live.OpenAsync(order, fx.Identity.CustomerToken(customer));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var reading = LiveClient.ReadAsync(response, TimeSpan.FromSeconds(10));
        await Task.Delay(300, LiveClient.Ct);
        await _live.ConfirmAsync(order);
        await Task.Delay(300, LiveClient.Ct);
        await _live.DeliverAsync(order);
        var events = Orders(await reading); // o servidor fecha sozinho no estado final

        Assert.Equal(["pending", "confirmed", "delivered"], events.Select(LiveClient.Status));
        Assert.Equal(3, events.Select(e => e.Id).Distinct().Count());
        foreach (var e in events)
        {
            ContractAssert.AssertSchema("orders.v1.yaml", "/components/schemas/Order", e.Data!);
        }
    }

    [Fact]
    public async Task Reconnect_WithTheLastEventId_DoesNotResendIt_AndAFinalOneIs204()
    {
        var (order, customer) = await _live.PlaceAsync();
        var token = fx.Identity.CustomerToken(customer);
        string pendingId;
        using (var first = await _live.OpenAsync(order, token))
        {
            pendingId = Orders(await LiveClient.ReadAsync(first, TimeSpan.FromSeconds(5), e => e.Count > 0 && Orders(e).Count == 1))[0].Id!;
        }

        using var again = await _live.OpenAsync(order, token, lastEventId: pendingId);
        var reading = LiveClient.ReadAsync(again, TimeSpan.FromSeconds(10));
        await Task.Delay(300, LiveClient.Ct);
        await _live.ConfirmAsync(order);
        await Task.Delay(300, LiveClient.Ct);
        await _live.DeliverAsync(order);
        var events = Orders(await reading);

        // o pending que o cliente já tinha não volta
        Assert.Equal(["confirmed", "delivered"], events.Select(LiveClient.Status));
        using var afterTheEnd = await _live.OpenAsync(order, token, lastEventId: events[^1].Id);
        Assert.Equal(HttpStatusCode.NoContent, afterTheEnd.StatusCode);
    }

    [Fact]
    public async Task QuietOrder_GetsHeartbeats_AndTheStreamEndsAfterTheMaxDuration()
    {
        var (order, customer) = await _live.PlaceAsync();
        using var response = await _live.OpenAsync(order, fx.Identity.CustomerToken(customer));

        var events = await LiveClient.ReadAsync(response, TimeSpan.FromSeconds(10)); // MaxDuration de 3 s no teste

        Assert.Single(Orders(events));
        Assert.Contains(events, e => e.Comment == "keep-alive");
    }

    [Fact]
    public async Task OnlyTheOwnerOrAnOperator_CanWatch()
    {
        var (order, customer) = await _live.PlaceAsync();

        using var anonymous = await _live.OpenAsync(order, null);
        using var stranger = await _live.OpenAsync(order, fx.Identity.CustomerToken());
        using var missing = await _live.OpenAsync(Guid.NewGuid(), fx.Identity.CustomerToken(customer));
        using var operatorView = await _live.OpenAsync(order, fx.Identity.AdminToken());

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal("application/problem+json", stranger.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, operatorView.StatusCode);
    }

    [Fact]
    public async Task ClosedStreams_LeaveNoSubscription()
    {
        var feed = fx.Factory.Services.GetRequiredService<OrderChangeFeed>();
        var (order, customer) = await _live.PlaceAsync();
        using (var response = await _live.OpenAsync(order, fx.Identity.CustomerToken(customer)))
        {
            await LiveClient.ReadAsync(response, TimeSpan.FromSeconds(5), e => Orders(e).Count == 1);
        }

        await Task.Delay(500, LiveClient.Ct); // o servidor percebe a desconexão
        Assert.Equal(0, feed.Count);
    }
}

/// <summary>Releitura praticamente desligada: a mudança só chega pelo aviso (o que o listener do Kafka faz).</summary>
public sealed class SlowPollOrdersFixture : OrdersFixture
{
    protected override string LivePollInterval => "00:01:00";
}

public sealed class OrderLiveNotificationTests(SlowPollOrdersFixture fx) : IClassFixture<SlowPollOrdersFixture>
{
    private readonly LiveClient _live = new(fx);

    // confirmado não é o fim (o pedido segue o fornecedor): o teste para de ler ali
    private static bool UntilConfirmed(List<SseEvent> events) =>
        events.LastOrDefault(e => e.Event == "order") is { } last && LiveClient.Status(last) == "confirmed";

    [Fact]
    public async Task Notification_PushesTheChangeRightAway()
    {
        var (order, customer) = await _live.PlaceAsync();
        using var response = await _live.OpenAsync(order, fx.Identity.CustomerToken(customer));
        var reading = LiveClient.ReadAsync(response, TimeSpan.FromSeconds(10), UntilConfirmed);
        await Task.Delay(300, LiveClient.Ct);

        await _live.ConfirmAsync(order);
        var started = DateTimeOffset.UtcNow;
        fx.Factory.Services.GetRequiredService<OrderChangeFeed>().Notify(order); // o que o OrderChangeListener faz
        var events = (await reading).Where(e => e.Event == "order").ToList();

        Assert.Equal(["pending", "confirmed"], events.Select(LiveClient.Status));
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5), "sem o aviso, só sairia na releitura de 1 min");
    }

    [Fact]
    public async Task EachSagaStep_ArrivesOnCommit_WithoutWaitingForKafka()
    {
        var (order, customer) = await _live.PlaceAsync();
        using var response = await _live.OpenAsync(order, fx.Identity.CustomerToken(customer));
        var reading = LiveClient.ReadAsync(response, TimeSpan.FromSeconds(10), UntilConfirmed);
        await Task.Delay(300, LiveClient.Ct);
        var started = DateTimeOffset.UtcNow;

        await _live.ReserveAsync(order);
        await Task.Delay(300, LiveClient.Ct);
        await _live.ConfirmAsync(order);
        var events = (await reading).Where(e => e.Event == "order").ToList();

        // sem Notify manual: quem avisa é o commit (OrderCommitNotifier)
        Assert.Equal(["pending", "pending", "confirmed"], events.Select(LiveClient.Status));
        var steps = events.Select(e => JsonDocument.Parse(e.Data!).RootElement.GetProperty("stock_reserved_at").ValueKind).ToList();
        Assert.Equal([JsonValueKind.Null, JsonValueKind.String, JsonValueKind.String], steps);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5), "sem o aviso, só sairia na releitura de 1 min");
    }

    [Fact]
    public async Task Feed_CollapsesRepeatedNotifications_AndForgetsDisposedSubscriptions()
    {
        var feed = new OrderChangeFeed();
        var order = Guid.NewGuid();
        var subscription = feed.Subscribe(order);

        feed.Notify(order);
        feed.Notify(order);
        feed.Notify(Guid.NewGuid()); // de outro pedido

        Assert.True(await subscription.WaitAsync(TimeSpan.FromSeconds(1), LiveClient.Ct));
        Assert.False(await subscription.WaitAsync(TimeSpan.FromMilliseconds(100), LiveClient.Ct)); // os dois viraram um
        subscription.Dispose();
        Assert.Equal(0, feed.Count);
    }
}
