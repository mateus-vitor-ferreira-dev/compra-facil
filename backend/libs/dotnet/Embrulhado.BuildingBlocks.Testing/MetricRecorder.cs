using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Embrulhado.BuildingBlocks.Testing;

/// <summary>
/// Escuta um contador (pelo meter e pelo nome do instrumento) enquanto existe, para o teste conferir o que a
/// métrica contou. O listener vê o processo inteiro: com classes de teste em paralelo usando o mesmo
/// contador, conferir com "pelo menos".
/// </summary>
public sealed class MetricRecorder : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<(long Value, KeyValuePair<string, object?>[] Tags)> _measurements = new();

    public MetricRecorder(string meterName, string instrumentName)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) => _measurements.Enqueue((value, tags.ToArray())));
        _listener.Start();
    }

    /// <summary>A soma do que foi contado com estas tags (as outras tags da medição não importam).</summary>
    public long Total(params (string Key, string Value)[] tags) =>
        _measurements
            .Where(m => tags.All(t => m.Tags.Any(kv => kv.Key == t.Key && Equals(kv.Value?.ToString(), t.Value))))
            .Sum(m => m.Value);

    public void Dispose() => _listener.Dispose();
}
