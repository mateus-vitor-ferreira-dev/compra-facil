using System.Collections.Concurrent;
using System.Threading.Channels;
using CompraFacil.Orders.Domain;
using Microsoft.Extensions.Options;

namespace CompraFacil.Orders.Application.Orders;

/// <summary>Acompanhamento ao vivo do pedido (seção Orders:Live).</summary>
public sealed class OrderLiveOptions
{
    public const string Section = "Orders:Live";

    /// <summary>Releitura do pedido sem aviso: cobre o Kafka fora (o aviso só antecipa).</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Comentário SSE sem dado, para o gateway e proxies não fecharem a conexão parada.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Duração máxima de um stream; o cliente reconecta (EventSource faz sozinho) com Last-Event-ID.</summary>
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Avisos de "este pedido mudou" para os streams abertos nesta réplica. Quem avisa é o
/// OrderChangeListener (Infrastructure), que lê o tópico de pedidos: a mudança feita em qualquer
/// réplica chega a todas. O aviso não leva o estado; o stream relê o pedido no banco.
/// </summary>
public sealed class OrderChangeFeed
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Subscription, byte>> _byOrder = new();

    public Subscription Subscribe(Guid orderId)
    {
        var subscription = new Subscription(this, orderId);
        _byOrder.GetOrAdd(orderId, _ => new()).TryAdd(subscription, 0);
        return subscription;
    }

    public void Notify(Guid orderId)
    {
        if (_byOrder.TryGetValue(orderId, out var subscriptions))
        {
            foreach (var s in subscriptions.Keys)
            {
                s.Signal();
            }
        }
    }

    /// <summary>Streams abertos (para a métrica e os testes).</summary>
    public int Count => _byOrder.Values.Sum(s => s.Count);

    private void Remove(Subscription subscription)
    {
        if (_byOrder.TryGetValue(subscription.OrderId, out var subscriptions))
        {
            subscriptions.TryRemove(subscription, out _);
            if (subscriptions.IsEmpty)
            {
                _byOrder.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<Subscription, byte>>(subscription.OrderId, subscriptions));
            }
        }
    }

    public sealed class Subscription : IDisposable
    {
        private readonly OrderChangeFeed _feed;

        // um aviso pendente basta: vários seguidos viram uma releitura só
        private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

        internal Subscription(OrderChangeFeed feed, Guid orderId)
        {
            _feed = feed;
            OrderId = orderId;
        }

        public Guid OrderId { get; }

        internal void Signal() => _signals.Writer.TryWrite(true);

        /// <summary>Espera um aviso por até <paramref name="timeout"/>; true se chegou.</summary>
        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                return await _signals.Reader.ReadAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return false;
            }
        }

        public void Dispose() => _feed.Remove(this);
    }
}

/// <summary>Um pedido sendo acompanhado: o estado atual e a espera pela próxima mudança.</summary>
public sealed class OrderWatch(OrderChangeFeed.Subscription subscription, IOrderRepository orders, Order current, TimeSpan pollInterval) : IDisposable
{
    public Order Current { get; private set; } = current;

    /// <summary>
    /// Espera um aviso (ou o intervalo de releitura) e relê o pedido. Devolve o pedido se ele mudou;
    /// nulo se continua igual.
    /// </summary>
    public async Task<Order?> NextAsync(TimeSpan maxWait, CancellationToken ct)
    {
        await subscription.WaitAsync(maxWait < pollInterval ? maxWait : pollInterval, ct);
        var latest = await orders.GetAsync(Current.Id, ct);
        if (latest is null || EventId(latest) == EventId(Current))
        {
            return null;
        }
        Current = latest;
        return latest;
    }

    /// <summary>Id do evento SSE: muda a cada transição; o cliente o devolve em Last-Event-ID.</summary>
    public static string EventId(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return order.UpdatedAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static bool IsFinal(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return order.Status is OrderStatus.Confirmed or OrderStatus.Cancelled;
    }

    public void Dispose() => subscription.Dispose();
}

/// <summary>Abre o acompanhamento: assina os avisos antes de ler, para não perder uma mudança no meio.</summary>
public sealed class OrderLive(OrderChangeFeed feed, OrderQueries queries, IOrderRepository orders, IOptions<OrderLiveOptions> options)
{
    public OrderLiveOptions Options => options.Value;

    /// <exception cref="BuildingBlocks.Web.NotFoundException">Pedido inexistente ou de outro cliente.</exception>
    public async Task<OrderWatch> OpenAsync(Guid orderId, OrderReader reader, CancellationToken ct)
    {
        var subscription = feed.Subscribe(orderId);
        try
        {
            var order = await queries.GetAsync(orderId, reader, ct);
            return new OrderWatch(subscription, orders, order, options.Value.PollInterval);
        }
        catch
        {
            subscription.Dispose();
            throw;
        }
    }
}
