namespace CompraFacil.BuildingBlocks.Messaging;

/// <summary>Configuração da mensageria (seção Kafka do appsettings + o que o serviço declara).</summary>
public sealed class MessagingOptions
{
    /// <summary>Nome do serviço: producer do envelope, consumer group e chave do lock do relay.</summary>
    public string ServiceName { get; set; } = "";

    /// <summary>Brokers; no compose, redpanda:9092 (Kafka:BootstrapServers).</summary>
    public string BootstrapServers { get; set; } = "localhost:19092";

    /// <summary>Tempo para o broker confirmar um lote; estourou, o lote volta para o outbox.</summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Espera do relay quando o outbox está vazio.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Teto da espera entre tentativas quando a publicação falha.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(10);

    public int BatchSize { get; set; } = 100;

    /// <summary>Liga o relay e o consumer (desligue em testes que não precisam de Kafka).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Tópicos de que o serviço é dono: o relay os cria com a configuração do contrato.</summary>
    public IList<string> ProducedTopics { get; } = [];

    internal List<HandlerRegistration> Handlers { get; } = [];
}

/// <summary>Handler de um (tópico, event_type): desserializa data no tipo certo e chama o handler.</summary>
internal sealed record HandlerRegistration(
    string Topic,
    string EventType,
    Func<IServiceProvider, EventEnvelope, EventContext, CancellationToken, Task> Invoke);
