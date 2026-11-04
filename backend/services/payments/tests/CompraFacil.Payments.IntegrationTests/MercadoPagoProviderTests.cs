using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CompraFacil.Payments.Application.Payments;
using CompraFacil.Payments.Domain;
using CompraFacil.Payments.Infrastructure.Provider;
using Microsoft.Extensions.Options;

namespace CompraFacil.Payments.IntegrationTests;

/// <summary>
/// O que o adaptador manda ao Mercado Pago e como lê a resposta, sem rede: o que a documentação dele pede e o
/// payment-sim não confere por inteiro (o formato da data, o pagador, os status de disputa, o manifesto).
/// </summary>
public sealed class MercadoPagoProviderTests
{
    private const string Secret = "segredo-do-webhook";

    private static readonly DateTimeOffset s_expires = new DateTimeOffset(2026, 11, 4, 10, 30, 0, TimeSpan.FromHours(-3)).AddTicks(1234567);

    private sealed class Capture(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public JsonElement Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content is not null)
            {
                Body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct)).RootElement.Clone();
            }
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    private static (MercadoPagoProvider Provider, Capture Handler) Create(string response, HttpStatusCode status = HttpStatusCode.Created)
    {
        var handler = new Capture(status, response);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        var options = Options.Create(new MercadoPagoOptions { AccessToken = "APP_USR-teste", WebhookSecret = Secret });
        return (new MercadoPagoProvider(http, options), handler);
    }

    private const string PixPending = """
        {"id": 123, "status": "pending", "status_detail": "pending_waiting_transfer",
         "date_of_expiration": "2026-11-04T10:30:00.000-03:00",
         "point_of_interaction": {"transaction_data": {"qr_code": "000201", "qr_code_base64": "iVBOR", "ticket_url": "https://mp/t"}}}
        """;

    [Fact]
    public async Task Pix_SendsPayerWithCpfAndExpirationInMilliseconds()
    {
        var (provider, handler) = Create(PixPending);

        var result = await provider.CreatePixAsync(new PixRequest(
            "k1", Guid.NewGuid(), new Money(12990, "BRL"), "ana@exemplo.com", s_expires, new Payer("Ana Maria Souza", "52998224725")),
            CancellationToken.None);

        Assert.Equal(ProviderStatus.Pending, result.Status);
        var body = handler.Body;
        Assert.Equal("pix", body.GetProperty("payment_method_id").GetString());
        Assert.Equal(129.90m, body.GetProperty("transaction_amount").GetDecimal());
        // a API lê no máximo milissegundos; o .NET mandaria sete casas
        Assert.Equal("2026-11-04T10:30:00.123-03:00", body.GetProperty("date_of_expiration").GetString());
        var payer = body.GetProperty("payer");
        Assert.Equal("ana@exemplo.com", payer.GetProperty("email").GetString());
        Assert.Equal("Ana", payer.GetProperty("first_name").GetString());
        Assert.Equal("Maria Souza", payer.GetProperty("last_name").GetString());
        Assert.Equal("CPF", payer.GetProperty("identification").GetProperty("type").GetString());
        Assert.Equal("52998224725", payer.GetProperty("identification").GetProperty("number").GetString());
    }

    [Fact]
    public async Task Pix_WithoutPayer_SendsOnlyTheEmail()
    {
        var (provider, handler) = Create(PixPending);

        await provider.CreatePixAsync(new PixRequest("k1", Guid.NewGuid(), new Money(5000, "BRL"), "ana@exemplo.com", s_expires),
            CancellationToken.None);

        var payer = handler.Body.GetProperty("payer");
        Assert.Equal("ana@exemplo.com", payer.GetProperty("email").GetString());
        Assert.False(payer.TryGetProperty("identification", out _));
        Assert.False(payer.TryGetProperty("first_name", out _));
    }

    [Fact]
    public async Task Card_SingleName_HasNoLastName()
    {
        var (provider, handler) = Create("""{"id": 9, "status": "authorized", "status_detail": "pending_capture", "card": {"last_four_digits": "1234"}}""");

        var result = await provider.AuthorizeCardAsync(new CardRequest(
            "k2", Guid.NewGuid(), new Money(5000, "BRL"), "ana@exemplo.com", "tok", "visa", 3, new Payer("Ana", "52998224725")),
            CancellationToken.None);

        Assert.Equal(ProviderStatus.Authorized, result.Status);
        Assert.Equal("1234", result.CardLastFour);
        var payer = handler.Body.GetProperty("payer");
        Assert.Equal("Ana", payer.GetProperty("first_name").GetString());
        Assert.False(payer.TryGetProperty("last_name", out _));
        Assert.False(handler.Body.GetProperty("capture").GetBoolean());
        Assert.False(handler.Body.TryGetProperty("date_of_expiration", out _));
    }

    [Theory]
    [InlineData("in_mediation")]
    [InlineData("charged_back")]
    public async Task Disputes_AreReadAsDisputed(string status)
    {
        var (provider, _) = Create($$"""{"id": 7, "status": "{{status}}", "status_detail": "x"}""", HttpStatusCode.OK);

        var result = await provider.GetAsync("7", CancellationToken.None);

        Assert.Equal(ProviderStatus.Disputed, result.Status);
    }

    private static string Signature(string manifest, string ts = "1704908010") =>
        $"ts={ts},v1={Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(manifest)))}";

    [Fact]
    public void Signature_FollowsTheManifestTemplate()
    {
        var (provider, _) = Create("{}");

        Assert.True(provider.VerifyNotification("123", "req-1", Signature("id:123;request-id:req-1;ts:1704908010;")));
        Assert.False(provider.VerifyNotification("124", "req-1", Signature("id:123;request-id:req-1;ts:1704908010;")));
        Assert.False(provider.VerifyNotification("123", "req-1", "ts=1704908010,v1=nao-e-hex"));
        Assert.False(provider.VerifyNotification("123", "req-1", null));
    }

    [Fact]
    public void Signature_LowercasesTheIdAndSkipsTheMissingRequestId()
    {
        var (provider, _) = Create("{}");

        Assert.True(provider.VerifyNotification("ABC123", "req-1", Signature("id:abc123;request-id:req-1;ts:1704908010;")));
        Assert.True(provider.VerifyNotification("123", null, Signature("id:123;ts:1704908010;")));
    }
}
