namespace CompraFacil.BuildingBlocks.Messaging;

/// <summary>
/// De onde vem o fluxo atual, para os eventos publicados nele: o correlation_id (X-Request-ID da
/// requisição que começou o fluxo) e o causation_id (event_id do evento sendo tratado, se houver).
/// Preenchido pelo RequestIdMiddleware (HTTP) e pelo consumer Kafka.
/// </summary>
public sealed record CorrelationContext(string CorrelationId, Guid? CausationId)
{
    private static readonly AsyncLocal<CorrelationContext?> s_current = new();

    public static CorrelationContext? Current
    {
        get => s_current.Value;
        set => s_current.Value = value;
    }
}
