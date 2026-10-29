using System.Net.Http.Json;
using CompraFacil.Inventory.Application.Stock;
using Microsoft.Extensions.Logging;

namespace CompraFacil.Inventory.Infrastructure.Supplier;

/// <summary>Seção Fulfillment: onde fica a conferência ao vivo no fornecedor (#218).</summary>
public sealed class FulfillmentOptions
{
    public const string Section = "Fulfillment";

    /// <summary>Raiz do fulfillment na rede dos serviços (ex.: http://fulfillment:8007/); vazio desliga a conferência.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>Quanto a reserva espera pela conferência antes de seguir pelo espelho.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// A conferência pelo fulfillment (POST /v1/internal/supplier-stock/check). Qualquer falha (fora do ar,
/// timeout, 503 do fornecedor) devolve nulo: a reserva segue pelo espelho.
/// </summary>
internal sealed partial class FulfillmentStockCheck(HttpClient http, TimeProvider clock, ILogger<FulfillmentStockCheck> logger) : ISupplierStockCheck
{
    // a API da loja é snake_case (contracts/openapi/fulfillment.v1.yaml)
    private static readonly System.Text.Json.JsonSerializerOptions s_json = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<IReadOnlyList<LiveSupplierStock>?> CheckAsync(IReadOnlyList<(string Vid, int Quantity)> items, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("v1/internal/supplier-stock/check",
                new CheckRequest([.. items.Select(i => new CheckItem(i.Vid, i.Quantity))]), s_json, ct);
            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, $"respondeu {(int)response.StatusCode}");
                return null;
            }
            var body = await response.Content.ReadFromJsonAsync<CheckResponse>(s_json, ct);
            var now = clock.GetUtcNow();
            return body?.Items is { } lines ? [.. lines.Where(l => l.Known).Select(l => new LiveSupplierStock(l.Vid, l.Available, now))] : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            LogFailed(logger, e.Message);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "conferência no fornecedor falhou: {Reason}")]
    private static partial void LogFailed(ILogger logger, string reason);

    private sealed record CheckItem(string Vid, int Quantity);

    private sealed record CheckRequest(IReadOnlyList<CheckItem> Items);

    private sealed record CheckLine(string Vid, int Available, bool Known);

    private sealed record CheckResponse(IReadOnlyList<CheckLine>? Items);
}

/// <summary>Sem o fulfillment configurado: a reserva usa só o espelho.</summary>
internal sealed class NoSupplierStockCheck : ISupplierStockCheck
{
    public Task<IReadOnlyList<LiveSupplierStock>?> CheckAsync(IReadOnlyList<(string Vid, int Quantity)> items, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LiveSupplierStock>?>(null);
}
