using System.Net;
using System.Text;
using System.Text.Json;
using CompraFacil.Fulfillment.Application.Invoices;
using CompraFacil.Fulfillment.Domain;
using CompraFacil.Fulfillment.Infrastructure.Invoices;
using Microsoft.Extensions.Options;

namespace CompraFacil.Fulfillment.IntegrationTests;

/// <summary>O cliente da Nuvem Fiscal contra respostas HTTP simuladas: token, emissão, consulta, rejeição e cancelamento.</summary>
public sealed class NuvemFiscalInvoiceIssuerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Stub(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery, string Body, string? Auth)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body, request.Headers.Authorization?.Parameter));
            return respond(request, body);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static readonly string s_key = "31261011222333000181550010000010011000010015";

    private static object Authorized(string id = "nfe_1") => new
    {
        id,
        status = "autorizado",
        chave = s_key,
        numero = 1001,
        serie = 1,
        autorizacao = new { codigo_status = 100, motivo_status = "Autorizado o uso da NF-e", data_evento = "2026-10-29T15:00:00Z" },
    };

    private static (NuvemFiscalInvoiceIssuer Issuer, Stub Stub) Issuer(Func<HttpRequestMessage, string, HttpResponseMessage> api)
    {
        var stub = new Stub((req, body) => req.RequestUri!.AbsolutePath == "/oauth/token"
            ? Json(new { access_token = "tok-" + Guid.NewGuid().ToString("N")[..6], token_type = "bearer", expires_in = 3600 })
            : api(req, body));
        var options = Options.Create(new InvoiceOptions { AuthUrl = new Uri("http://sim/oauth/token"), BaseUrl = new Uri("http://sim/") });
        var http = new HttpClient(stub) { BaseAddress = options.Value.BaseUrl };
        return (new NuvemFiscalInvoiceIssuer(http, new NuvemFiscalTokenCache(new Factory(stub), options, TimeProvider.System), options, TimeProvider.System), stub);
    }

    private static Invoice Invoice(string state = "SP", string payment = "card", long freight = 0) => Domain.Invoice.Create(
        Guid.NewGuid(), Guid.NewGuid(), "ana@exemplo.com",
        new ShipTo("Ana Souza", "52998224725", "11987654321", "01310100", "Avenida Paulista", "1000", "Apto 12", "Bela Vista", "São Paulo", state),
        [new InvoiceLine("MOU-1", "Mouse", "84716053", 2, 4995, 9990), new InvoiceLine("TEC-1", "Teclado", "84716052", 1, 15000, 15000)],
        payment, DateTimeOffset.UtcNow, freight);

    [Fact]
    public async Task Emite_com_as_tags_da_NFe_depois_de_procurar_pela_referencia()
    {
        var (issuer, stub) = Issuer((req, _) => req.Method == HttpMethod.Get ? Json(new { data = Array.Empty<object>() }) : Json(Authorized()));
        var invoice = Invoice();

        var result = await issuer.IssueAsync(invoice, Ct);

        Assert.Equal(new InvoiceIssue.Authorized("nfe_1", s_key, 1001, 1, DateTimeOffset.Parse("2026-10-29T15:00:00Z", System.Globalization.CultureInfo.InvariantCulture)), result);
        Assert.Equal($"/nfe?ambiente=homologacao&referencia={invoice.Id}", stub.Requests[1].PathAndQuery);
        Assert.NotNull(stub.Requests[1].Auth);
        var post = stub.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery == "/nfe");
        using var body = JsonDocument.Parse(post.Body);
        var inf = body.RootElement.GetProperty("infNFe");
        Assert.Equal(invoice.Id.ToString(), body.RootElement.GetProperty("referencia").GetString());
        Assert.Equal(("52998224725", "SP"), (inf.GetProperty("dest").GetProperty("CPF").GetString(),
            inf.GetProperty("dest").GetProperty("enderDest").GetProperty("UF").GetString()));
        // a loja é de MG e o cliente, de SP: venda à ordem interestadual
        Assert.Equal(("6120", 2), (inf.GetProperty("det")[0].GetProperty("prod").GetProperty("CFOP").GetString(),
            inf.GetProperty("ide").GetProperty("idDest").GetInt32()));
        Assert.Equal(249.9m, inf.GetProperty("total").GetProperty("ICMSTot").GetProperty("vNF").GetDecimal());
        Assert.Equal("03", inf.GetProperty("pag").GetProperty("detPag")[0].GetProperty("tPag").GetString());
    }

    [Fact]
    public async Task Mesmo_estado_usa_o_CFOP_interno_e_o_PIX()
    {
        var (issuer, stub) = Issuer((req, _) => req.Method == HttpMethod.Get ? Json(new { data = Array.Empty<object>() }) : Json(Authorized()));
        await issuer.IssueAsync(Invoice(state: "MG", payment: "pix"), Ct);
        using var body = JsonDocument.Parse(stub.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery == "/nfe").Body);
        var inf = body.RootElement.GetProperty("infNFe");
        Assert.Equal("5120", inf.GetProperty("det")[0].GetProperty("prod").GetProperty("CFOP").GetString());
        Assert.Equal("17", inf.GetProperty("pag").GetProperty("detPag")[0].GetProperty("tPag").GetString());
    }

    [Fact]
    public async Task Nota_ja_emitida_numa_tentativa_anterior_nao_e_emitida_de_novo()
    {
        var (issuer, stub) = Issuer((req, _) => req.Method == HttpMethod.Get ? Json(new { data = new[] { Authorized("nfe_antiga") } }) : Json(Authorized()));
        var result = await issuer.IssueAsync(Invoice(), Ct);
        Assert.Equal("nfe_antiga", Assert.IsType<InvoiceIssue.Authorized>(result).ProviderId);
        Assert.DoesNotContain(stub.Requests, r => r.Method == HttpMethod.Post && r.PathAndQuery == "/nfe");
    }

    [Fact]
    public async Task Rejeicao_traz_o_cStat_e_pendente_ou_5xx_e_passageiro()
    {
        var (rejecting, _) = Issuer((req, _) => req.Method == HttpMethod.Get
            ? Json(new { data = Array.Empty<object>() })
            : Json(new { id = "nfe_r", status = "rejeitado", autorizacao = new { codigo_status = 237, motivo_status = "Rejeição: CPF do destinatário inválido" } }));
        Assert.Equal(new InvoiceIssue.Rejected("nfe_r", 237, "Rejeição: CPF do destinatário inválido"), await rejecting.IssueAsync(Invoice(), Ct));

        var (pending, _) = Issuer((req, _) => req.Method == HttpMethod.Get ? Json(new { data = new[] { new { id = "nfe_p", status = "pendente" } } }) : Json(Authorized()));
        await Assert.ThrowsAsync<InvoiceIssuerUnavailableException>(() => pending.IssueAsync(Invoice(), Ct));

        var (down, _) = Issuer((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<InvoiceIssuerUnavailableException>(() => down.IssueAsync(Invoice(), Ct));
    }

    [Fact]
    public async Task Cancelamento_manda_a_justificativa_e_a_recusa_vira_erro_proprio()
    {
        var (issuer, stub) = Issuer((_, _) => Json(new { id = "evt_1", status = "registrado", codigo_status = 135 }));
        await issuer.CancelAsync("nfe_1", InvoiceCanceller.Justification, Ct);
        var cancel = stub.Requests.Single(r => r.PathAndQuery == "/nfe/nfe_1/cancelamento");
        Assert.Contains("estornado ao cliente", cancel.Body, StringComparison.Ordinal);

        var (late, _) = Issuer((_, _) => Json(new { error = new { code = "UnprocessableEntity", message = "prazo de cancelamento vencido" } }, HttpStatusCode.UnprocessableEntity));
        var e = await Assert.ThrowsAsync<InvoiceCancelRefusedException>(() => late.CancelAsync("nfe_1", InvoiceCanceller.Justification, Ct));
        Assert.Contains("prazo", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Token_vencido_e_renovado_uma_vez()
    {
        var calls = 0;
        var (issuer, stub) = Issuer((_, _) => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<nfeProc/>") });
        Assert.Equal("<nfeProc/>", await issuer.GetXmlAsync("nfe_1", Ct));
        Assert.Equal(2, stub.Requests.Count(r => r.PathAndQuery == "/oauth/token"));
    }

    [Fact]
    public async Task Frete_entra_no_total_e_rateado_nos_itens()
    {
        var (issuer, stub) = Issuer((req, _) => req.Method == HttpMethod.Get ? Json(new { data = Array.Empty<object>() }) : Json(Authorized()));
        await issuer.IssueAsync(Invoice(freight: 2431), Ct);
        using var body = JsonDocument.Parse(stub.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery == "/nfe").Body);
        var inf = body.RootElement.GetProperty("infNFe");
        var total = inf.GetProperty("total").GetProperty("ICMSTot");
        Assert.Equal((249.9m, 24.31m, 274.21m), (total.GetProperty("vProd").GetDecimal(), total.GetProperty("vFrete").GetDecimal(), total.GetProperty("vNF").GetDecimal()));
        var perItem = inf.GetProperty("det").EnumerateArray().Sum(d => d.GetProperty("prod").GetProperty("vFrete").GetDecimal());
        Assert.Equal(24.31m, perItem);
        Assert.Equal(274.21m, inf.GetProperty("pag").GetProperty("detPag")[0].GetProperty("vPag").GetDecimal());
    }
}
