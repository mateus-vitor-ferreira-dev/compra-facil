using System.Globalization;
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

    // o máximo do product/list; 100 páginas (20 mil itens) é o teto de um catálogo curado
    private const int FeedPageSize = 200;
    private const int MaxFeedPages = 100;

    /// <summary>Os valores do CJ são em dólar.</summary>
    private const string FeedCurrency = "USD";

    public string Name => options.Value.Name;

    public async Task<SupplierPlacement> PlaceOrderAsync(SupplierOrderRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        // sem o destino (pedido de antes do #221), vai só o e-mail, e o fornecedor recusa por falta de endereço
        var to = request.ShipTo;
        var body = new CreateOrderRequest(
            OrderNumber: request.OrderNumber.ToString(),
            ShippingCountryCode: "BR",
            ShippingProvince: to?.State ?? "",
            ShippingCity: to?.City ?? "",
            ShippingCustomerName: to?.Name ?? request.CustomerEmail,
            ShippingAddress: to is null ? "" : $"{to.Street}, {to.Number} - {to.District}",
            ShippingAddress2: to?.Complement ?? "",
            HouseNumber: to?.Number ?? "",
            ShippingZip: to?.ZipCode ?? "",
            ShippingPhone: to?.Phone ?? "",
            ConsigneeId: to?.Cpf ?? "",
            Email: request.CustomerEmail,
            Remark: Scenario(request.CustomerEmail),
            Products: [.. request.Items.Select(i => new CreateOrderProduct(i.Sku, i.Quantity))]);

        var envelope = await SendAsync<CreateOrderResult>(() => new HttpRequestMessage(HttpMethod.Post, "shopping/order/createOrderV2")
        {
            Content = JsonContent.Create(body, options: CjJson.Options),
        }, retryOnInvalidToken: true, ct);
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

    public async Task<IReadOnlyList<SupplierFeedItem>> ListProductsAsync(CancellationToken ct)
    {
        var items = new List<SupplierFeedItem>();
        for (var page = 1; page <= MaxFeedPages; page++)
        {
            var path = $"product/list?pageNum={page}&pageSize={FeedPageSize}";
            var envelope = await SendAsync<ProductPage>(() => new HttpRequestMessage(HttpMethod.Get, path), retryOnInvalidToken: true, ct);
            if (!envelope.Result || envelope.Data?.List is not { } list)
            {
                throw new SupplierUnavailableException($"o fornecedor não deu o feed: {envelope.Code} {envelope.Message}");
            }
            items.AddRange(list.Select(ToFeedItem).OfType<SupplierFeedItem>());
            if (list.Count < FeedPageSize || page * FeedPageSize >= envelope.Data.Total)
            {
                return items;
            }
        }
        throw new SupplierUnavailableException($"o feed do fornecedor passou de {MaxFeedPages} páginas");
    }

    // consultas de estoque simultâneas (uma por variante, como a API do CJ pede)
    private const int StockParallelism = 4;

    public async Task<IReadOnlyDictionary<string, int>> GetStockAsync(IReadOnlyCollection<string> vids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(vids);
        var stock = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        await Parallel.ForEachAsync(vids.Distinct(), new ParallelOptions { MaxDegreeOfParallelism = StockParallelism, CancellationToken = ct },
            async (vid, token) =>
            {
                var path = $"product/stock/queryByVid?vid={Uri.EscapeDataString(vid)}";
                var envelope = await SendAsync<IReadOnlyList<StockEntry>>(() => new HttpRequestMessage(HttpMethod.Get, path), retryOnInvalidToken: true, token);
                if (!envelope.Result || envelope.Data is not { } areas)
                {
                    throw new SupplierUnavailableException($"o fornecedor não deu o estoque de {vid}: {envelope.Code} {envelope.Message}");
                }
                // um vid pode estar em vários armazéns: vale a soma
                stock[vid] = areas.Sum(a => Math.Max(a.StorageNum, 0));
            });
        return stock;
    }

    /// <summary>Item sem vid ou sem preço válido não entra (sem custo não há preço de venda).</summary>
    private static SupplierFeedItem? ToFeedItem(FeedProduct p) =>
        string.IsNullOrWhiteSpace(p.Vid) || string.IsNullOrWhiteSpace(p.ProductNameEn)
            || !decimal.TryParse(p.SellPrice, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price) || price <= 0
            ? null
            : new SupplierFeedItem(p.Vid, p.ProductSku ?? p.Vid, p.ProductNameEn, p.CategoryName ?? "",
                (long)Math.Round(price * 100, MidpointRounding.AwayFromZero), FeedCurrency,
                string.IsNullOrWhiteSpace(p.ProductImage) ? null : p.ProductImage, p.ProductWeight);

    private async Task<Envelope<T>> SendAsync<T>(Func<HttpRequestMessage> request, bool retryOnInvalidToken, CancellationToken ct)
    {
        try
        {
            using var message = request();
            message.Headers.Add("CJ-Access-Token", await tokens.GetAsync(http, ct));
            using var response = await http.SendAsync(message, ct);
            if ((int)response.StatusCode >= 500)
            {
                throw new SupplierUnavailableException($"fornecedor respondeu {(int)response.StatusCode}");
            }
            var envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(CjJson.Options, ct)
                ?? throw new SupplierUnavailableException("resposta vazia do fornecedor");
            if (envelope.Code == InvalidToken || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                tokens.Invalidate();
                return retryOnInvalidToken
                    ? await SendAsync<T>(request, retryOnInvalidToken: false, ct)
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
        string OrderNumber, string ShippingCountryCode, string ShippingProvince, string ShippingCity, string ShippingCustomerName,
        string ShippingAddress, string ShippingAddress2, string HouseNumber, string ShippingZip, string ShippingPhone,
        [property: JsonPropertyName("consigneeID")] string ConsigneeId,
        string Email, string Remark, IReadOnlyList<CreateOrderProduct> Products);

    private sealed record CreateOrderResult(string? OrderId, string? OrderNumber, string? OrderStatus);

    private sealed record ProductPage(int Total, IReadOnlyList<FeedProduct>? List);

    private sealed record StockEntry(string? Vid, string? AreaId, int StorageNum);

    private sealed record FeedProduct(
        string? Vid, string? ProductSku, string? ProductNameEn, string? SellPrice, string? CategoryName, string? ProductImage, int? ProductWeight);
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
