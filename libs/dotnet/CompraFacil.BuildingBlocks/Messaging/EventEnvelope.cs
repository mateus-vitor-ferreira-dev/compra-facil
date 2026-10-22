using System.Text.Json;
using CompraFacil.BuildingBlocks.Web;

namespace CompraFacil.BuildingBlocks.Messaging;

/// <summary>
/// Envelope de todo evento (EventEnvelope em contracts/asyncapi/common/v1/common.yaml). O conteúdo
/// específico vai em <see cref="Data"/>; o resto permite deduplicar (event_id), rastrear a causa e
/// escolher o handler (event_type + event_version).
/// </summary>
public sealed record EventEnvelope(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAt,
    string Producer,
    string CorrelationId,
    Guid? CausationId,
    JsonElement Data)
{
    /// <summary>JSON dos eventos: snake_case, igual aos contratos e ao catalog.</summary>
    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ServiceDefaults.ConfigureJson(options);
        return options;
    }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Lê o envelope; nulo se o JSON não é um envelope válido (sem event_id ou event_type).</summary>
    public static EventEnvelope? TryParse(string json)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<EventEnvelope>(json, JsonOptions);
            return envelope is { EventId: var id, EventType.Length: > 0 } && id != Guid.Empty ? envelope : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public T DataAs<T>() =>
        Data.Deserialize<T>(JsonOptions) ?? throw new JsonException($"data vazio em {EventType} ({EventId})");
}
