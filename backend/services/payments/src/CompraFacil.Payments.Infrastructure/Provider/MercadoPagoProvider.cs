using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CompraFacil.Payments.Application.Payments;
using CompraFacil.Payments.Domain;
using Microsoft.Extensions.Options;

namespace CompraFacil.Payments.Infrastructure.Provider;

/// <summary>Seção Payments:MercadoPago: a API do provedor (em dev e no CI, o payment-sim).</summary>
public sealed class MercadoPagoOptions
{
    public const string Section = "Payments:MercadoPago";

    /// <summary>Raiz da API, com a barra no fim (ex.: http://payment-sim:8090/).</summary>
    public Uri BaseUrl { get; set; } = new("http://localhost:8092/");

    public string AccessToken { get; set; } = "TEST-sim-access-token";

    /// <summary>A chave do x-signature das notificações (no painel do Mercado Pago, a "assinatura secreta").</summary>
    public string WebhookSecret { get; set; } = "sim-secret";

    /// <summary>Para onde o provedor manda as notificações (o webhook do payments, pelo gateway).</summary>
    public Uri? NotificationUrl { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// O provedor pela API do Mercado Pago (v1/payments): PIX com QR e expiração, cartão por token com
/// autorização e captura, cancelamento, estorno total e parcial, todos com X-Idempotency-Key. Validado
/// contra o payment-sim; antes de apontar para o Mercado Pago de verdade, conferir os campos na
/// documentação dele.
/// </summary>
internal sealed class MercadoPagoProvider(HttpClient http, IOptions<MercadoPagoOptions> options) : IPaymentProvider
{
    // o Mercado Pago usa snake_case e valores em reais com ponto decimal
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public string Name => "mercadopago";

    public Task<ProviderPayment> CreatePixAsync(PixRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(HttpMethod.Post, "v1/payments", request.IdempotencyKey, new CreatePayment(
            Reais(request.Amount), $"Pedido {request.OrderId}", "pix", null, null, null, request.OrderId.ToString(),
            options.Value.NotificationUrl?.AbsoluteUri, request.ExpiresAt, new Payer(request.PayerEmail)), ct);
    }

    public Task<ProviderPayment> AuthorizeCardAsync(CardRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(HttpMethod.Post, "v1/payments", request.IdempotencyKey, new CreatePayment(
            Reais(request.Amount), $"Pedido {request.OrderId}", request.CardBrand, request.CardToken, request.Installments,
            false, request.OrderId.ToString(), options.Value.NotificationUrl?.AbsoluteUri, null, new Payer(request.PayerEmail)), ct);
    }

    public Task<ProviderPayment> GetAsync(string reference, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"v1/payments/{Uri.EscapeDataString(reference)}", null, null, ct);

    public Task<ProviderPayment> CaptureAsync(string reference, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"v1/payments/{Uri.EscapeDataString(reference)}", null, new { capture = true }, ct);

    public Task<ProviderPayment> CancelAsync(string reference, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"v1/payments/{Uri.EscapeDataString(reference)}", null, new { status = "cancelled" }, ct);

    public async Task<ProviderRefund> RefundAsync(string reference, long? amountCents, string idempotencyKey, CancellationToken ct)
    {
        using var response = await CallAsync(HttpMethod.Post, $"v1/payments/{Uri.EscapeDataString(reference)}/refunds", idempotencyKey,
            amountCents is { } cents ? new { amount = cents / 100m } : null, ct);
        var refund = await ReadAsync<MpRefund>(response, ct);
        return new ProviderRefund(refund.Id.ToString(CultureInfo.InvariantCulture), (long)Math.Round(refund.Amount * 100m));
    }

    /// <summary>x-signature "ts=…,v1=…": HMAC-SHA256 de "id:&lt;data.id&gt;;request-id:&lt;x-request-id&gt;;ts:&lt;ts&gt;;".</summary>
    public bool VerifyNotification(string dataId, string? requestId, string? signature)
    {
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(options.Value.WebhookSecret))
        {
            return false;
        }
        string? ts = null, v1 = null;
        foreach (var part in signature.Split(',', StringSplitOptions.TrimEntries))
        {
            var (key, value) = (part.Split('=', 2) is [var k, var v] ? (k, v) : ("", ""));
            if (key == "ts")
            {
                ts = value;
            }
            else if (key == "v1")
            {
                v1 = value;
            }
        }
        if (ts is null || v1 is null)
        {
            return false;
        }
        var manifest = $"id:{dataId};request-id:{requestId};ts:{ts};";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.WebhookSecret), Encoding.UTF8.GetBytes(manifest));
        byte[] received;
        try
        {
            received = Convert.FromHexString(v1);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    private async Task<ProviderPayment> SendAsync(HttpMethod method, string path, string? idempotencyKey, object? body, CancellationToken ct)
    {
        using var response = await CallAsync(method, path, idempotencyKey, body, ct);
        return ToProvider(await ReadAsync<MpPayment>(response, ct));
    }

    private async Task<HttpResponseMessage> CallAsync(HttpMethod method, string path, string? idempotencyKey, object? body, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.AccessToken);
        if (idempotencyKey is not null)
        {
            message.Headers.Add("X-Idempotency-Key", idempotencyKey);
        }
        if (body is not null)
        {
            message.Content = JsonContent.Create(body, options: s_json);
        }
        try
        {
            var response = await http.SendAsync(message, ct);
            if ((int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Unauthorized)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw new PaymentProviderUnavailableException($"o provedor respondeu {status}");
            }
            return response;
        }
        catch (HttpRequestException e)
        {
            throw new PaymentProviderUnavailableException($"provedor inacessível: {e.Message}", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new PaymentProviderUnavailableException("o provedor não respondeu a tempo", e);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<MpError>(s_json, ct);
            // 4xx é erro nosso (token usado, estado que não permite): não se conserta tentando de novo
            throw new PaymentProviderRejectedException($"o provedor recusou a operação ({(int)response.StatusCode} {error?.Error}): {error?.Message}");
        }
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(s_json, ct)
                ?? throw new PaymentProviderUnavailableException("resposta vazia do provedor");
        }
        catch (JsonException e)
        {
            throw new PaymentProviderUnavailableException("resposta do provedor fora do formato", e);
        }
    }

    private static decimal Reais(Money amount) => amount.AmountCents / 100m;

    /// <summary>O status do Mercado Pago no da loja.</summary>
    private static ProviderPayment ToProvider(MpPayment p)
    {
        var reference = p.Id.ToString(CultureInfo.InvariantCulture);
        var status = (p.Status, p.StatusDetail) switch
        {
            ("pending", _) => ProviderStatus.Pending,
            ("in_process", _) => ProviderStatus.InProcess,
            ("authorized", _) => ProviderStatus.Authorized,
            ("approved", _) => ProviderStatus.Approved,
            ("rejected", _) => ProviderStatus.Rejected,
            ("cancelled", "expired") => ProviderStatus.Expired,
            ("cancelled", _) => ProviderStatus.Cancelled,
            ("refunded", _) => ProviderStatus.Refunded,
            _ => throw new PaymentProviderUnavailableException($"status desconhecido do provedor: {p.Status}/{p.StatusDetail}"),
        };
        FailureReason? reason = status == ProviderStatus.Rejected
            ? p.StatusDetail switch
            {
                "cc_rejected_insufficient_amount" => FailureReason.InsufficientFunds,
                "cc_rejected_high_risk" => FailureReason.FraudSuspected,
                _ => FailureReason.CardDeclined,
            }
            : null;
        var td = p.PointOfInteraction?.TransactionData;
        var pix = td is { QrCode: { } code } && p.DateOfExpiration is { } expires
            ? new PixCharge(code, td.QrCodeBase64 ?? "", td.TicketUrl, expires)
            : null;
        return new ProviderPayment(reference, status, reason, pix, p.Card?.LastFourDigits);
    }

    private sealed record Payer(string? Email);

    private sealed record CreatePayment(
        decimal TransactionAmount, string Description, string PaymentMethodId, string? Token, int? Installments, bool? Capture,
        string ExternalReference, string? NotificationUrl, DateTimeOffset? DateOfExpiration, Payer Payer);

    private sealed record MpTransactionData(string? QrCode, string? QrCodeBase64, string? TicketUrl);

    private sealed record MpPointOfInteraction(MpTransactionData? TransactionData);

    private sealed record MpCard(string? LastFourDigits);

    private sealed record MpPayment(
        long Id, string? Status, string? StatusDetail, DateTimeOffset? DateOfExpiration, MpPointOfInteraction? PointOfInteraction, MpCard? Card);

    private sealed record MpRefund(long Id, decimal Amount);

    private sealed record MpError(string? Message, string? Error);
}
