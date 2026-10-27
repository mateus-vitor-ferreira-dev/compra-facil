using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace CompraFacil.BuildingBlocks.Messaging;

/// <summary>Criação dos tópicos de que o serviço é dono (os do contrato, os de retry e a DLT).</summary>
internal static class KafkaTopics
{
    // configuração dos tópicos do contrato AsyncAPI (bindings kafka)
    public const int Partitions = 3;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    /// <summary>A DLT guarda mais: dá tempo de investigar e reprocessar.</summary>
    public static readonly TimeSpan DeadLetterRetention = TimeSpan.FromDays(30);

    /// <summary>Cria os tópicos (3 partições, delete); existente não é alterado.</summary>
    public static async Task EnsureAsync(string brokers, IEnumerable<(string Name, TimeSpan Retention)> topics, TimeSpan timeout)
    {
        var specs = topics.Select(t => new TopicSpecification
        {
            Name = t.Name,
            NumPartitions = Partitions,
            ReplicationFactor = -1, // padrão do broker
            Configs = new()
            {
                ["retention.ms"] = t.Retention.TotalMilliseconds.ToString("F0", null),
                ["cleanup.policy"] = "delete",
            },
        }).ToList();
        if (specs.Count == 0)
        {
            return;
        }
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = brokers }).Build();
        try
        {
            await admin.CreateTopicsAsync(specs, new CreateTopicsOptions { RequestTimeout = timeout });
        }
        catch (CreateTopicsException e) when (e.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
            // já existiam: ok
        }
    }
}
