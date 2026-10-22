namespace CompraFacil.Inventory.Application.Stock;

/// <summary>
/// Concorrência otimista: se outra transação mudou o saldo, relê e tenta de novo. Poucas tentativas
/// bastam (o conflito é só no mesmo produto, no mesmo instante); esgotadas, a exceção sobe e o
/// consumer tenta a mensagem de novo com backoff.
/// </summary>
internal static class Retry
{
    public const int MaxAttempts = 5;

    public static async Task<T> OnConflictAsync<T>(IStockRepository stock, Func<Task<T>> attempt)
    {
        for (var i = 1; ; i++)
        {
            try
            {
                return await attempt();
            }
            catch (ConcurrencyConflictException) when (i < MaxAttempts)
            {
                stock.Reset();
                await Task.Delay(Random.Shared.Next(5, 20 * i)); // jitter: não colidem de novo juntos
            }
        }
    }
}
