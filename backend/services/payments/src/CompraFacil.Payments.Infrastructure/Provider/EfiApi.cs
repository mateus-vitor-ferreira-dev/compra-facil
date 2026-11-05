using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CompraFacil.Payments.Application.Payments;
using Microsoft.Extensions.Options;

namespace CompraFacil.Payments.Infrastructure.Provider;

/// <summary>Seção Payments:Efi: as duas APIs da Efí (em dev e no CI, o payment-sim nas duas).</summary>
public sealed class EfiOptions
{
    public const string Section = "Payments:Efi";

    /// <summary>API Cobranças, a do cartão (https://cobrancas.api.efipay.com.br/; homologação: cobrancas-h).</summary>
    public Uri ChargesBaseUrl { get; set; } = new("http://localhost:8092/");

    /// <summary>API Pix (https://pix.api.efipay.com.br/; homologação: pix-h). Exige o certificado da conta.</summary>
    public Uri PixBaseUrl { get; set; } = new("http://localhost:8092/");

    /// <summary>As credenciais da aplicação (as mesmas nas duas APIs).</summary>
    public string ClientId { get; set; } = "Client_Id_sim";

    public string ClientSecret { get; set; } = "Client_Secret_sim";

    /// <summary>
    /// O certificado .p12 que a Efí gera para a conta (em base64), obrigatório em toda chamada da API Pix, inclusive
    /// na autorização. Não é o A1 da nota fiscal. Vazio: sem mTLS (o simulador).
    /// </summary>
    public string? PixCertificateBase64 { get; set; }

    public string? PixCertificatePassword { get; set; }

    /// <summary>A chave PIX da conta que recebe.</summary>
    public string PixKey { get; set; } = "7d9f0335-8dcc-4054-9bf9-0dbd61d36906";

    /// <summary>Para onde a Efí manda as notificações do cartão (o POST com o token), pelo gateway.</summary>
    public Uri? ChargeNotificationUrl { get; set; }

    /// <summary>
    /// O webhook do PIX, cadastrado na Efí ao subir (PUT /v2/webhook/:chave), com o segredo
    /// <see cref="PixWebhookSecret"/> na URL. Vazio: não cadastra (o PIX expira pelo job, e o pagamento não chega).
    /// </summary>
    public Uri? PixWebhookUrl { get; set; }

    /// <summary>Segredo que vai na URL do webhook do PIX (?hmac=) e que o payments confere em cada aviso.</summary>
    public string? PixWebhookSecret { get; set; }

    /// <summary>
    /// Cadastra o webhook sem a validação de mTLS da Efí (x-skip-mtls-checking): o gateway não pede certificado de
    /// cliente. A proteção fica no segredo da URL e na consulta à Efí, que é a verdade de cada aviso.
    /// </summary>
    public bool PixWebhookSkipMtls { get; set; } = true;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
}

internal enum EfiApiKind
{
    Charges,
    Pix,
}

/// <summary>Um 4xx da Efí, com o código e o nome do erro (Cobranças: code/error; Pix: nome).</summary>
internal sealed class EfiApiException(HttpStatusCode status, int? code, string? name, string message)
    : Exception($"a Efí recusou ({(int)status} {code?.ToString(CultureInfo.InvariantCulture) ?? name}): {message}")
{
    public HttpStatusCode Status { get; } = status;
    public int? Code { get; } = code;
    public string? Name { get; } = name;

    public PaymentProviderRejectedException Rejected() => new(Message);
}

/// <summary>
/// As chamadas às duas APIs da Efí: o token de cada uma (Basic → Bearer, renovado antes de vencer e de novo num
/// 401), os 4xx como <see cref="EfiApiException"/> e o resto (5xx, 429, rede, tempo) como provedor fora do ar. A
/// Efí às vezes responde 401 em texto puro e 500 para erro de configuração: nada disso é lido como JSON.
/// </summary>
internal sealed class EfiApi(IHttpClientFactory http, IOptions<EfiOptions> options, TimeProvider clock)
{
    public const string ChargesClient = "efi-charges";
    public const string PixClient = "efi-pix";

    internal static readonly JsonSerializerOptions ChargesJson = Json(JsonNamingPolicy.SnakeCaseLower);
    internal static readonly JsonSerializerOptions PixJson = Json(JsonNamingPolicy.CamelCase);

    private readonly Token _charges = new();
    private readonly Token _pix = new();

    private static JsonSerializerOptions Json(JsonNamingPolicy naming) => new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = naming,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // a Efí manda a validade do PIX como número na criação e como texto na consulta
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Chama a API e lê o corpo como <typeparamref name="T"/>.</summary>
    public async Task<T> SendAsync<T>(
        EfiApiKind api, HttpMethod method, string path, object? body, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null)
    {
        var json = api == EfiApiKind.Charges ? ChargesJson : PixJson;
        for (var attempt = 1; ; attempt++)
        {
            var token = await TokenAsync(api, ct);
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            foreach (var (name, value) in headers ?? new Dictionary<string, string>())
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, body.GetType(), options: json);
            }
            using var response = await CallAsync(api, request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                (api == EfiApiKind.Charges ? _charges : _pix).Clear();
                if (attempt == 1)
                {
                    continue; // o token venceu antes do previsto: um novo, uma vez
                }
                throw new PaymentProviderUnavailableException("a Efí recusou as credenciais (401)");
            }
            var text = await response.Content.ReadAsStringAsync(ct);
            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new PaymentProviderUnavailableException($"a Efí respondeu {(int)response.StatusCode}: {Truncate(text)}");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw ErrorOf(response.StatusCode, text);
            }
            try
            {
                return JsonSerializer.Deserialize<T>(text, json) ?? throw new PaymentProviderUnavailableException("resposta vazia da Efí");
            }
            catch (JsonException e)
            {
                throw new PaymentProviderUnavailableException("resposta da Efí fora do formato", e);
            }
        }
    }

    private async Task<HttpResponseMessage> CallAsync(EfiApiKind api, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.CreateClient(api == EfiApiKind.Charges ? ChargesClient : PixClient).SendAsync(request, ct);
        }
        catch (HttpRequestException e)
        {
            throw new PaymentProviderUnavailableException($"Efí inacessível: {e.Message}", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new PaymentProviderUnavailableException("a Efí não respondeu a tempo", e);
        }
    }

    private async Task<string> TokenAsync(EfiApiKind api, CancellationToken ct)
    {
        var slot = api == EfiApiKind.Charges ? _charges : _pix;
        var now = clock.GetUtcNow();
        if (slot.Valid(now) is { } cached)
        {
            return cached;
        }
        await slot.Lock.WaitAsync(ct);
        try
        {
            if (slot.Valid(clock.GetUtcNow()) is { } again)
            {
                return again;
            }
            var o = options.Value;
            using var request = new HttpRequestMessage(HttpMethod.Post, api == EfiApiKind.Charges ? "v1/authorize" : "oauth/token")
            {
                Content = JsonContent.Create(new { grant_type = "client_credentials" }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.ClientId}:{o.ClientSecret}")));
            using var response = await CallAsync(api, request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // credencial errada também é "fora do ar" para a loja: tentar de novo e alertar, não recusar o cliente
                throw new PaymentProviderUnavailableException($"a Efí não autorizou a aplicação ({(int)response.StatusCode})");
            }
            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(ChargesJson, ct)
                ?? throw new PaymentProviderUnavailableException("autorização vazia da Efí");
            // renova um minuto antes de vencer (o de Cobranças vale 10 min; o do Pix, 1 h)
            slot.Set(auth.AccessToken, clock.GetUtcNow().AddSeconds(Math.Max(auth.ExpiresIn - 60, 30)));
            return auth.AccessToken;
        }
        finally
        {
            slot.Lock.Release();
        }
    }

    /// <summary>O 4xx nos dois formatos: Cobranças ({code, error, error_description} texto ou objeto) e Pix ({nome, mensagem}, ou o do BCB).</summary>
    internal static EfiApiException ErrorOf(HttpStatusCode status, string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new EfiApiException(status, null, null, Truncate(text));
            }
            int? code = root.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : null;
            var name = Str(root, "error") ?? Str(root, "nome") ?? Str(root, "title");
            var message = root.TryGetProperty("error_description", out var d)
                ? d.ValueKind == JsonValueKind.Object ? $"{Str(d, "property")}: {Str(d, "message")}" : d.ToString()
                : Str(root, "mensagem") ?? Str(root, "detail") ?? Truncate(text);
            return new EfiApiException(status, code, name, message);
        }
        catch (JsonException)
        {
            return new EfiApiException(status, null, null, Truncate(text));
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300];

    private sealed record AuthResponse(string AccessToken, int ExpiresIn);

    private sealed class Token
    {
        private string? _value;
        private DateTimeOffset _expires;

        public SemaphoreSlim Lock { get; } = new(1, 1);

        public string? Valid(DateTimeOffset now) => _value is not null && now < _expires ? _value : null;

        public void Set(string value, DateTimeOffset expires) => (_value, _expires) = (value, expires);

        public void Clear() => _value = null;
    }
}
