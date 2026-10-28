using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Domain;
using Microsoft.Extensions.Options;

namespace CompraFacil.Fulfillment.Infrastructure.Supplier;

/// <summary>
/// O fornecedor pela API do CJ Dropshipping (v2): token por e-mail e senha, criação de pedido idempotente
/// pelo número do pedido, resposta num envelope { code, result, message, data }. Validado contra o
/// fornecedor simulado; antes de apontar para o CJ de verdade, conferir os campos na documentação dele.
/// </summary>
internal sealed class CjSupplierGateway(HttpClient http, CjTokenCache tokens, IOptions<SupplierOptions> options) : ISupplierGateway
{
    private const int InvalidToken = 1600001;
    private const int InvalidParams = 1600100;
    private const int OutOfStock = 1603001;
    private const int OrderRejected = 1603002;

    public string Name => options.Value.Name;

    public async Task<SupplierPlacement> PlaceOrderAsync(SupplierOrderRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new CreateOrderRequest(
            OrderNumber: request.OrderNumber.ToString(),
            ShippingCountryCode: "BR",
            // o endereço de entrega entra no pedido com o checkout completo (#221, #222)
            ShippingCustomerName: request.CustomerEmail,
            Email: request.CustomerEmail,
            Remark: Scenario(request.CustomerEmail),
            Products: [.. request.Items.Select(i => new CreateOrderProduct(i.Sku, i.Quantity))]);

        var envelope = await SendAsync(body, retryOnInvalidToken: true, ct);
        if (envelope.Result && envelope.Data is { } data && !string.IsNullOrEmpty(data.OrderId))
        {
            return new SupplierPlacement.Accepted(data.OrderId);
        }
        return envelope.Code switch
        {
            OutOfStock => new SupplierPlacement.Rejected(SupplierFailureReasons.OutOfStock, envelope.Message ?? "sem estoque"),
            OrderRejected or InvalidParams => new SupplierPlacement.Rejected(SupplierFailureReasons.Rejected, envelope.Message ?? "recusado"),
            // código que não conhecemos: melhor tentar de novo do que recusar um pedido pago
            _ => throw new SupplierUnavailableException($"resposta inesperada do fornecedor: {envelope.Code} {envelope.Message}"),
        };
    }

    private async Task<Envelope<CreateOrderResult>> SendAsync(CreateOrderRequest body, bool retryOnInvalidToken, CancellationToken ct)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "shopping/order/createOrderV2")
            {
                Content = JsonContent.Create(body, options: CjJson.Options),
            };
            message.Headers.Add("CJ-Access-Token", await tokens.GetAsync(http, ct));
            using var response = await http.SendAsync(message, ct);
            if ((int)response.StatusCode >= 500)
            {
                throw new SupplierUnavailableException($"fornecedor respondeu {(int)response.StatusCode}");
            }
            var envelope = await response.Content.ReadFromJsonAsync<Envelope<CreateOrderResult>>(CjJson.Options, ct)
                ?? throw new SupplierUnavailableException("resposta vazia do fornecedor");
            if (envelope.Code == InvalidToken || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                tokens.Invalidate();
                return retryOnInvalidToken
                    ? await SendAsync(body, retryOnInvalidToken: false, ct)
                    : throw new SupplierUnavailableException("o fornecedor recusou o token");
            }
            return envelope;
        }
        catch (HttpRequestException e)
        {
            throw new SupplierUnavailableException($"fornecedor inacessível: {e.Message}", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new SupplierUnavailableException("o fornecedor não respondeu a tempo", e);
        }
        catch (JsonException e)
        {
            throw new SupplierUnavailableException("resposta do fornecedor fora do formato", e);
        }
    }

    /// <summary>"ana+recusar-depois@x" → "[recusar-depois]", só com ScenarioFromEmail (o simulado).</summary>
    private string Scenario(string email)
    {
        if (!options.Value.ScenarioFromEmail)
        {
            return "";
        }
        var local = email.Split('@')[0];
        var plus = local.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? "" : $"[{local[(plus + 1)..]}]";
    }

    private sealed record CreateOrderProduct(string Vid, int Quantity);

    private sealed record CreateOrderRequest(
        string OrderNumber, string ShippingCountryCode, string ShippingCustomerName, string Email, string Remark, IReadOnlyList<CreateOrderProduct> Products);

    private sealed record CreateOrderResult(string? OrderId, string? OrderNumber, string? OrderStatus);
}

/// <summary>O envelope de toda resposta do CJ.</summary>
internal sealed record Envelope<T>(int Code, bool Result, string? Message, T? Data);

internal static class CjJson
{
    /// <summary>O CJ usa camelCase nos campos.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>O token de acesso, guardado entre chamadas e renovado antes de vencer (ou quando é recusado).</summary>
internal sealed class CjTokenCache(IOptions<SupplierOptions> options, TimeProvider clock) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetAsync(HttpClient http, CancellationToken ct)
    {
        if (_token is { } valid && clock.GetUtcNow() < _expiresAt)
        {
            return valid;
        }
        await _lock.WaitAsync(ct);
        try
        {
            if (_token is { } fresh && clock.GetUtcNow() < _expiresAt)
            {
                return fresh;
            }
            using var response = await http.PostAsJsonAsync("authentication/getAccessToken",
                new { email = options.Value.Email, password = options.Value.Password }, CjJson.Options, ct);
            var envelope = await response.Content.ReadFromJsonAsync<Envelope<TokenResult>>(CjJson.Options, ct);
            if (envelope is not { Result: true, Data.AccessToken: { } token })
            {
                throw new SupplierUnavailableException($"o fornecedor não deu o token: {envelope?.Message}");
            }
            _token = token;
            // renova antes de vencer: um dia antes, ou na metade do prazo se ele for curto
            var now = clock.GetUtcNow();
            var expiry = envelope.Data.AccessTokenExpiryDate ?? now.AddHours(1);
            var margin = TimeSpan.FromTicks(Math.Min(TimeSpan.FromDays(1).Ticks, (expiry - now).Ticks / 2));
            _expiresAt = expiry - margin;
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate() => _token = null;

    public void Dispose() => _lock.Dispose();

    private sealed record TokenResult(string? AccessToken, DateTimeOffset? AccessTokenExpiryDate);
}
