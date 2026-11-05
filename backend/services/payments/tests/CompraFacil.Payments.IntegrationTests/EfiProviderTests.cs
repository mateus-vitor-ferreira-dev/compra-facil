using System.Net;
using System.Text;
using System.Text.Json;
using CompraFacil.Payments.Application.Payments;
using CompraFacil.Payments.Domain;
using CompraFacil.Payments.Infrastructure.Provider;
using Microsoft.Extensions.Options;

namespace CompraFacil.Payments.IntegrationTests;

/// <summary>
/// O adaptador da Efí contra respostas copiadas da documentação dela (dev.efipay.com.br, 2026-11): o que vai no
/// corpo, e as esquisitices que o payment-sim imita (401 em texto puro, recusa com HTTP 200, erro em dois formatos,
/// a validade do PIX como texto na consulta).
/// </summary>
public sealed class EfiProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Call(HttpMethod Method, string Path, string? Body, string? Authorization, IReadOnlyDictionary<string, string> Headers);

    /// <summary>Responde por "MÉTODO caminho"; guarda cada chamada. Sem rota, 404.</summary>
    private sealed class Server : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];
        public Dictionary<string, Queue<(HttpStatusCode, string)>> Routes { get; } = new();

        public void On(string route, HttpStatusCode status, string body)
        {
            if (!Routes.TryGetValue(route, out var q))
            {
                Routes[route] = q = new();
            }
            q.Enqueue((status, body));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            Calls.Add(new Call(request.Method, request.RequestUri!.AbsolutePath, body, request.Headers.Authorization?.ToString(), headers));
            var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
            if (!Routes.TryGetValue(key, out var q) || q.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            }
            var (status, text) = q.Count > 1 ? q.Dequeue() : q.Peek();
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, text.StartsWith('{') || text.StartsWith('[') ? "application/json" : "text/html") };
        }
    }

    private sealed class Factory(Server server) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(server, disposeHandler: false)
        {
            BaseAddress = new Uri(name == EfiApi.ChargesClient ? "https://cobrancas-h.api.efipay.com.br/" : "https://pix-h.api.efipay.com.br/"),
        };
    }

    private static (EfiProvider Provider, Server Server) Create()
    {
        var server = new Server();
        server.On("POST /v1/authorize", HttpStatusCode.OK, """{"access_token":"tok-cob","refresh_token":"r","expires_in":600,"expire_at":"1690986856033","token_type":"Bearer"}""");
        server.On("POST /oauth/token", HttpStatusCode.OK, """{"access_token":"tok-pix","token_type":"Bearer","expires_in":3600,"scope":"cob.write"}""");
        var options = Options.Create(new EfiOptions
        {
            ClientId = "Client_Id_x",
            ClientSecret = "Client_Secret_x",
            PixKey = "chave-da-loja",
            ChargeNotificationUrl = new Uri("https://api.loja/payments/v1/webhooks/efi/charges"),
        });
        var api = new EfiApi(new Factory(server), options, TimeProvider.System);
        return (new EfiProvider(api, options, TimeProvider.System), server);
    }

    private static readonly Guid s_order = Guid.Parse("0192f1a2-b3c4-7d5e-8f70-8192a3b4c5d6");
    private static readonly Payer s_payer = new("Ana D'Ávila Souza", "52998224725", "35999990000",
        new BillingAddress("37200000", "Rua A", "10", null, "Centro", "Lavras", "mg"));

    private static string Cob(string status, string expiracao, bool loc = true) => $$"""
        {"calendario":{"criacao":"{{DateTimeOffset.UtcNow.AddMinutes(-1):yyyy-MM-ddTHH:mm:ss.fffZ}}","expiracao":{{expiracao}}},
         "txid":"cf0192f1a2b3c47d5e8f708192a3b4c5d6","revisao":0,{{(loc ? "\"loc\":{\"id\":789,\"location\":\"x\",\"tipoCob\":\"cob\"}," : "")}}
         "location":"x","status":"{{status}}","valor":{"original":"129.90"},"chave":"chave-da-loja",
         "pixCopiaECola":"00020101021226830014BR.GOV.BCB.PIX2561qrcodespix.sejaefi.com.br/v2/41e0badf6304ABCD",
         "pix":[{"endToEndId":"E12345678202009091221kkkkkkkkkkk","txid":"cf","valor":"129.90","horario":"2020-09-09T20:15:00.358Z",
                 "devolucoes":[{"id":"x","rtrId":"D1","valor":"9.90","horario":{"solicitacao":"2020-09-09T20:15:00.358Z"},"status":"DEVOLVIDO"}]}]}
        """;

    [Fact]
    public async Task Pix_CreatesTheCobWithTheOrderTxid_AndAPngQr()
    {
        var (provider, server) = Create();
        server.On("PUT /v2/cob/cf0192f1a2b3c47d5e8f708192a3b4c5d6", HttpStatusCode.Created, Cob("ATIVA", "1800"));
        server.On("GET /v2/loc/789/qrcode", HttpStatusCode.OK, """{"qrcode":"000201","imagemQrcode":"data:image/svg+xml;base64,PHN2Zz4=","linkVisualizacao":"https://pix.sejaefi.com.br/cob/pagar/abc"}""");

        var result = await provider.CreatePixAsync(new PixRequest(s_order.ToString(), s_order, new Money(12990, "BRL"), "ana@exemplo.com",
            DateTimeOffset.UtcNow.AddMinutes(30), s_payer), Ct);

        Assert.Equal(("cf0192f1a2b3c47d5e8f708192a3b4c5d6", ProviderStatus.Pending), (result.Reference, result.Status));
        Assert.StartsWith("iVBORw0KGgo", result.Pix!.QrCodeBase64, StringComparison.Ordinal); // PNG, não o SVG da Efí
        Assert.Equal("https://pix.sejaefi.com.br/cob/pagar/abc", result.Pix.TicketUrl);
        var put = server.Calls.Single(c => c.Method == HttpMethod.Put);
        Assert.Equal("Bearer tok-pix", put.Authorization);
        var body = JsonDocument.Parse(put.Body!).RootElement;
        Assert.Equal("129.90", body.GetProperty("valor").GetProperty("original").GetString());
        Assert.Equal("chave-da-loja", body.GetProperty("chave").GetString());
        Assert.Equal("52998224725", body.GetProperty("devedor").GetProperty("cpf").GetString());
        Assert.InRange(body.GetProperty("calendario").GetProperty("expiracao").GetInt32(), 1790, 1800);
        var basic = server.Calls.Single(c => c.Path == "/oauth/token").Authorization!;
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("Client_Id_x:Client_Secret_x")), basic);
    }

    [Fact]
    public async Task Pix_Repeated_ReadsTheSameCob_AndTheExpiredIsExpired()
    {
        var (provider, server) = Create();
        server.On("PUT /v2/cob/cf0192f1a2b3c47d5e8f708192a3b4c5d6", HttpStatusCode.Conflict,
            """{"nome":"txid_duplicado","mensagem":"Campo txid informado já foi utilizado em outra cobrança"}""");
        server.On("GET /v2/cob/cf0192f1a2b3c47d5e8f708192a3b4c5d6", HttpStatusCode.OK, Cob("ATIVA", "\"30\"", loc: false));

        var result = await provider.CreatePixAsync(new PixRequest("k", s_order, new Money(12990, "BRL"), null, DateTimeOffset.UtcNow.AddMinutes(30)), Ct);

        // a validade veio como texto, e a cobrança criada há 1 min com 30 s já venceu (continua ATIVA na Efí)
        Assert.Equal(ProviderStatus.Expired, result.Status);
        Assert.Null(result.Pix!.TicketUrl);
    }

    [Fact]
    public async Task PixRefund_UsesAnIdFromTheKey_AndTheDuplicateIsRead()
    {
        var (provider, server) = Create();
        server.On("GET /v2/cob/cfabc0000000000000000000000000000", HttpStatusCode.OK, Cob("CONCLUIDA", "1800"));
        var id = EfiProvider.RefundId("pagamento:cancel");
        var path = $"/v2/pix/E12345678202009091221kkkkkkkkkkk/devolucao/{id}";
        server.On($"PUT {path}", HttpStatusCode.Conflict, """{"nome":"devolucao_id_duplicado","mensagem":"O id informado já foi utilizado em outra devolução"}""");
        server.On($"GET {path}", HttpStatusCode.OK, """{"id":"x","rtrId":"D99","valor":"120.00","horario":{"solicitacao":"2020-09-11T15:25:59.411Z"},"status":"EM_PROCESSAMENTO"}""");

        var refund = await provider.RefundAsync("cfabc0000000000000000000000000000", null, "pagamento:cancel", Ct);

        Assert.Equal(("D99", 12000L), (refund.Reference, refund.AmountCents));

        // a devolução que a Efí não realizou aparece na consulta da cobrança
        server.Routes["GET /v2/cob/cfabc0000000000000000000000000000"] = new([(HttpStatusCode.OK, Cob("CONCLUIDA", "1800")
            .Replace("\"status\":\"DEVOLVIDO\"", "\"status\":\"NAO_REALIZADO\"", StringComparison.Ordinal))]);
        Assert.Equal(["D1"], (await provider.GetAsync("cfabc0000000000000000000000000000", Ct)).FailedRefunds);
        Assert.Equal(32, id.Length);
        Assert.Contains("\"valor\":\"120.00\"", server.Calls.Single(c => c.Method == HttpMethod.Put).Body, StringComparison.Ordinal); // o pago menos o já devolvido
    }

    [Fact]
    public async Task Card_OpensThenPays_WithTheCustomerTheEfiRequires()
    {
        var (provider, server) = Create();
        server.On("POST /v1/charge", HttpStatusCode.OK, """{"code":200,"data":{"charge_id":391,"status":"new","total":12990,"custom_id":"x","created_at":"2026-11-05 10:00:00"}}""");
        server.On("POST /v1/charge/391/pay", HttpStatusCode.OK, """{"code":200,"data":{"installments":3,"installment_value":4330,"charge_id":391,"status":"approved","total":12990,"payment":"credit_card"}}""");
        server.On("GET /v1/charge/391", HttpStatusCode.OK, """{"code":200,"data":{"charge_id":391,"total":12990,"status":"approved","custom_id":"x","payment":{"method":"credit_card","credit_card":{"mask":"448578XXXXXX0087","installments":3}}}}""");
        var request = new CardRequest("k", s_order, new Money(12990, "BRL"), "ana@exemplo.com", new string('a', 40), "visa", 3, s_payer);

        var reference = await provider.OpenCardChargeAsync(request, Ct);
        var result = await provider.PayCardAsync(reference, request, Ct);

        Assert.Equal(("391", ProviderStatus.Authorized, "0087"), (result.Reference, result.Status, result.CardLastFour));
        var open = JsonDocument.Parse(server.Calls.Single(c => c.Path == "/v1/charge").Body!).RootElement;
        Assert.Equal(12990, open.GetProperty("items")[0].GetProperty("value").GetInt64());
        Assert.Equal(s_order.ToString(), open.GetProperty("metadata").GetProperty("custom_id").GetString());
        Assert.Equal("https://api.loja/payments/v1/webhooks/efi/charges", open.GetProperty("metadata").GetProperty("notification_url").GetString());
        var card = JsonDocument.Parse(server.Calls.Single(c => c.Path == "/v1/charge/391/pay").Body!).RootElement
            .GetProperty("payment").GetProperty("credit_card");
        var customer = card.GetProperty("customer");
        Assert.Equal("Ana D Ávila Souza", customer.GetProperty("name").GetString()); // só letras e espaços
        Assert.Equal(("52998224725", "35999990000", "ana@exemplo.com"),
            (customer.GetProperty("cpf").GetString(), customer.GetProperty("phone_number").GetString(), customer.GetProperty("email").GetString()));
        Assert.Equal(3, card.GetProperty("installments").GetInt32());
        Assert.Equal(("Centro", "MG"), (card.GetProperty("billing_address").GetProperty("neighborhood").GetString(),
            card.GetProperty("billing_address").GetProperty("state").GetString()));
    }

    [Fact]
    public async Task Card_PaidAgain_IsNotChargedTwice_AndTheRefusalIsNotAnError()
    {
        var (provider, server) = Create();
        server.On("POST /v1/charge/391/pay", HttpStatusCode.BadRequest,
            """{"code":3500030,"error":"validation_error","error_description":"A transação já possui forma de pagamento."}""");
        server.On("GET /v1/charge/391", HttpStatusCode.OK,
            """{"code":200,"data":{"charge_id":391,"total":2500,"status":"unpaid","reason":"Transação não autorizada por motivos de segurança."}}""");
        var request = new CardRequest("k", s_order, new Money(2500, "BRL"), "ana@exemplo.com", new string('a', 40), "visa", 1, s_payer);

        var result = await provider.PayCardAsync("391", request, Ct);

        Assert.Equal((ProviderStatus.Rejected, FailureReason.FraudSuspected), (result.Status, result.FailureReason));
    }

    [Fact]
    public async Task Errors_InBothFormats_AndThePlainText401RenewsTheToken()
    {
        var (provider, server) = Create();
        server.On("PUT /v1/charge/391/cancel", HttpStatusCode.BadRequest,
            """{"code":3500043,"error":"validation_error","error_description":"Apenas transações com status [new],[link],[waiting] ou [unpaid] podem ser canceladas"}""");
        var cancel = await Assert.ThrowsAsync<PaymentProviderRejectedException>(() => provider.CancelAsync("391", Ct));
        Assert.Contains("3500043", cancel.Message, StringComparison.Ordinal);

        var error = EfiApi.ErrorOf(HttpStatusCode.BadRequest,
            """{"code":3500034,"error":"validation_error","error_description":{"property":"/payment/credit_card/customer/phone_number","message":"inválido"}}""");
        Assert.Equal((3500034, "validation_error"), (error.Code, error.Name));
        Assert.Contains("phone_number: inválido", error.Message, StringComparison.Ordinal);

        // 401 em texto puro: um token novo e de novo
        server.On("GET /v1/charge/5", HttpStatusCode.Unauthorized, "Unauthorized");
        server.On("GET /v1/charge/5", HttpStatusCode.OK, """{"code":200,"data":{"charge_id":5,"total":100,"status":"paid"}}""");
        Assert.Equal(ProviderStatus.Approved, (await provider.GetAsync("5", Ct)).Status);
        Assert.Equal(2, server.Calls.Count(c => c.Path == "/v1/authorize"));

        // 500 (inclusive o de configuração da Efí) é fora do ar: tenta de novo, não recusa o cliente
        server.On("GET /v1/charge/6", HttpStatusCode.InternalServerError, """{"code":3500008,"error":"unauthorized_request","error_description":"Requisição não autorizada."}""");
        await Assert.ThrowsAsync<PaymentProviderUnavailableException>(() => provider.GetAsync("6", Ct));
    }

    [Fact]
    public async Task Notification_ListsTheChargesThatChanged()
    {
        var (provider, server) = Create();
        server.On("GET /v1/notification/09027955-5e06-4ff0-a9c7-46b47b8f1b27", HttpStatusCode.OK, """
            {"code":200,"data":[
              {"created_at":"2022-02-20 09:12:23","custom_id":null,"id":1,"identifiers":{"charge_id":24342333},"status":{"current":"new","previous":null},"type":"charge"},
              {"created_at":"2022-04-03 07:33:30","custom_id":null,"id":4,"identifiers":{"charge_id":24342333},"received_by_bank_at":"2022-04-02","status":{"current":"paid","previous":"unpaid"},"type":"charge","value":6990},
              {"created_at":"2022-04-03 07:33:30","custom_id":null,"id":5,"identifiers":{"subscription_id":1},"status":{"current":"active","previous":null},"type":"subscription"}]}
            """);

        Assert.Equal(["24342333"], await provider.ChangedByNotificationAsync("09027955-5e06-4ff0-a9c7-46b47b8f1b27", Ct));
    }

    [Theory]
    [InlineData("51 - Transação não autorizada, entre em contato com o emissor do cartão", FailureReason.InsufficientFunds)]
    [InlineData("Sistema de segurança: Os dados e comportamentos de utilização do cartão se assemelham a práticas e cenários de alto risco", FailureReason.FraudSuspected)]
    [InlineData("Dados do cartão inválidos.", FailureReason.CardDeclined)]
    public void RefusalReason_IsTranslated(string reason, FailureReason expected) => Assert.Equal(expected, EfiProvider.ReasonOf(reason));
}
