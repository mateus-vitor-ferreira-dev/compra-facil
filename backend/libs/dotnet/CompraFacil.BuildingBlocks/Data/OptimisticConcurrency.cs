namespace CompraFacil.BuildingBlocks.Data;

/// <summary>Outra transação mudou (ou criou) a linha entre a leitura e a gravação.</summary>
public sealed class ConcurrencyConflictException(string message, Exception inner) : Exception(message, inner);

/// <summary>
/// Concorrência otimista: se outra transação mudou o dado, descarta o que foi carregado, relê e tenta
/// de novo. Poucas tentativas bastam (o conflito é no mesmo registro, no mesmo instante); esgotadas,
/// a exceção sobe e o consumer Kafka tenta a mensagem de novo com backoff.
/// </summary>
public static class OptimisticConcurrency
{
    public const int MaxAttempts = 5;

    /// <param name="reset">Descarta o estado carregado (ex.: ChangeTracker.Clear) antes de tentar de novo.</param>
    /// <param name="attempt">Lê, decide e grava; lança <see cref="ConcurrencyConflictException"/> no conflito.</param>
    public static async Task<T> RetryAsync<T>(Action reset, Func<Task<T>> attempt)
    {
        ArgumentNullException.ThrowIfNull(reset);
        ArgumentNullException.ThrowIfNull(attempt);
        for (var i = 1; ; i++)
        {
            try
            {
                return await attempt();
            }
            catch (ConcurrencyConflictException) when (i < MaxAttempts)
            {
                reset();
                await Task.Delay(Random.Shared.Next(5, 20 * i)); // jitter: não colidem de novo juntos
            }
        }
    }
}
