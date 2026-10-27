using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Orders.Application.Orders;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CompraFacil.Orders.Infrastructure.Live;

/// <summary>
/// Lê o tópico de pedidos (orders.order.v1) do fim em diante e avisa os streams abertos nesta réplica.
/// Sem consumer group: cada réplica recebe todas as mensagens (fan-out), e não há offset a guardar,
/// porque só interessa o que acontece a partir de agora. A chave da mensagem é o id do pedido.
/// Kafka fora: os streams seguem pela releitura periódica (OrderLiveOptions.PollInterval).
/// </summary>
internal sealed partial class OrderChangeListener(
    OrderChangeFeed feed, MessagingOptions messaging, ILogger<OrderChangeListener> logger) : BackgroundService
{
    private static readonly TimeSpan s_retry = TimeSpan.FromSeconds(5);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // Consume() bloqueia: thread própria, como o EventConsumer
        Task.Factory.StartNew(() => RunAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Listen(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (KafkaException e)
            {
                LogUnavailable(logger, e.Error.Reason);
                await Task.Delay(s_retry, ct);
            }
        }
    }

    private void Listen(CancellationToken ct)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = messaging.BootstrapServers }).Build();
        var metadata = admin.GetMetadata(OrderEvents.Topic, TimeSpan.FromSeconds(5));
        var partitions = metadata.Topics.SingleOrDefault(t => t.Topic == OrderEvents.Topic && !t.Error.IsError)?.Partitions;
        if (partitions is not { Count: > 0 })
        {
            throw new KafkaException(new Error(ErrorCode.UnknownTopicOrPart, $"{OrderEvents.Topic} ainda não existe"));
        }

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = messaging.BootstrapServers,
            GroupId = "orders-live", // exigido pelo cliente; sem Subscribe nem Commit, o group não é usado
            ClientId = "orders-live",
            EnableAutoCommit = false,
            AllowAutoCreateTopics = false,
        }).Build();
        consumer.Assign(partitions.Select(p => new TopicPartitionOffset(OrderEvents.Topic, p.PartitionId, Offset.End)));
        LogListening(logger, partitions.Count);
        while (!ct.IsCancellationRequested)
        {
            var result = consumer.Consume(ct);
            if (Guid.TryParse(result?.Message?.Key, out var orderId))
            {
                feed.Notify(orderId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "acompanhamento ao vivo lendo orders.order.v1 ({Partitions} partições)")]
    private static partial void LogListening(ILogger logger, int partitions);

    [LoggerMessage(Level = LogLevel.Warning, Message = "acompanhamento ao vivo sem Kafka ({Reason}); os streams seguem pela releitura periódica")]
    private static partial void LogUnavailable(ILogger logger, string reason);
}
