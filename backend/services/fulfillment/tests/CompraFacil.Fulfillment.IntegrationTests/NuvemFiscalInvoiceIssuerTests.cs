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

    /// <summary>A API de sempre: o CEP de São Paulo, nenhuma nota pela referência e a emissão como <paramref name="emit"/> responde.</summary>
    private static Func<HttpRequestMessage, string, HttpResponseMessage> Api(Func<HttpResponseMessage>? emit = null, object[]? existing = null) =>
        (req, _) => req.RequestUri!.AbsolutePath switch
        {
            var path when path.StartsWith("/cep/", StringComparison.Ordinal) => Json(new { cep = path[5..], codigo_ibge = "3550308", municipio = "São Paulo", uf = "SP" }),
            "/nfe" when req.Method == HttpMethod.Get => Json(new { data = existing ?? [] }),
            "/nfe" => emit?.Invoke() ?? Json(Authorized()),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

    private static JsonElement Posted(Stub stub)
    {
        using var body = JsonDocument.Parse(stub.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery == "/nfe").Body);
        return body.RootElement.Clone();
    }

    private static (NuvemFiscalInvoiceIssuer Issuer, Stub Stub) Issuer(Func<HttpRequestMessage, string, HttpResponseMessage> api, string environment = "homologacao")
    {
        var stub = new Stub((req, body) => req.RequestUri!.AbsolutePath == "/oauth/token"
            ? Json(new { access_token = "tok-" + Guid.NewGuid().ToString("N")[..6], token_type = "bearer", expires_in = 3600 })
            : api(req, body));
        var options = Options.Create(new InvoiceOptions { AuthUrl = new Uri("http://sim/oauth/token"), BaseUrl = new Uri("http://sim/"), Environment = environment });
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
        var (issuer, stub) = Issuer(Api());
        var invoice = Invoice();

        var result = await issuer.IssueAsync(invoice, Ct);

        Assert.Equal(new InvoiceIssue.Authorized("nfe_1", s_key, 1001, 1, DateTimeOffset.Parse("2026-10-29T15:00:00Z", System.Globalization.CultureInfo.InvariantCulture)), result);
        Assert.Equal($"/nfe?cpf_cnpj=11222333000181&ambiente=homologacao&referencia={invoice.Id}", stub.Requests[1].PathAndQuery);
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
    public async Task Devolucao_e_nota_de_entrada_que_referencia_a_de_venda()
    {
        var (issuer, stub) = Issuer(Api());
        var invoice = Invoice();
        invoice.MarkAuthorized("nfe_venda", s_key, 1000, 1, DateTimeOffset.UtcNow);

        var result = await issuer.IssueReturnAsync(invoice, Ct);

        Assert.IsType<InvoiceIssue.Authorized>(result);
        Assert.Equal($"/nfe?cpf_cnpj=11222333000181&ambiente=homologacao&referencia={invoice.Id}-devolucao", stub.Requests[1].PathAndQuery);
        var post = stub.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery == "/nfe");
        using var body = JsonDocument.Parse(post.Body);
        var inf = body.RootElement.GetProperty("infNFe");
        var ide = inf.GetProperty("ide");
        Assert.Equal($"{invoice.Id}-devolucao", body.RootElement.GetProperty("referencia").GetString());
        Assert.Equal((0, 4), (ide.GetProperty("tpNF").GetInt32(), ide.GetProperty("finNFe").GetInt32()));
        Assert.Equal(s_key, ide.GetProperty("NFref")[0].GetProperty("refNFe").GetString());
        // a loja é de MG e o cliente, de SP: devolução interestadual; o estorno é fora da nota
        Assert.Equal("2202", inf.GetProperty("det")[0].GetProperty("prod").GetProperty("CFOP").GetString());
        var payment = inf.GetProperty("pag").GetProperty("detPag")[0];
        Assert.Equal(("90", 0m), (payment.GetProperty("tPag").GetString(), payment.GetProperty("vPag").GetDecimal()));
    }

    [Fact]
    public async Task Mesmo_estado_usa_o_CFOP_interno_e_o_PIX()
    {
        var (issuer, stub) = Issuer(Api());
        await issuer.IssueAsync(Invoice(state: "MG", payment: "pix"), Ct);
        using var body = JsonDocument.Parse(stub.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery == "/nfe").Body);
        var inf = body.RootElement.GetProperty("infNFe");
        Assert.Equal("5120", inf.GetProperty("det")[0].GetProperty("prod").GetProperty("CFOP").GetString());
        Assert.Equal("17", inf.GetProperty("pag").GetProperty("detPag")[0].GetProperty("tPag").GetString());
    }

    [Fact]
    public async Task Nota_ja_emitida_numa_tentativa_anterior_nao_e_emitida_de_novo()
    {
        var (issuer, stub) = Issuer(Api(existing: [Authorized("nfe_antiga")]));
        var result = await issuer.IssueAsync(Invoice(), Ct);
        Assert.Equal("nfe_antiga", Assert.IsType<InvoiceIssue.Authorized>(result).ProviderId);
        Assert.DoesNotContain(stub.Requests, r => r.Method == HttpMethod.Post && r.PathAndQuery == "/nfe");
    }

    [Fact]
    public async Task Rejeicao_traz_o_cStat_e_pendente_ou_5xx_e_passageiro()
    {
        var (rejecting, _) = Issuer(Api(() =>
            Json(new { id = "nfe_r", status = "rejeitado", autorizacao = new { codigo_status = 237, motivo_status = "Rejeição: CPF do destinatário inválido" } })));
        Assert.Equal(new InvoiceIssue.Rejected("nfe_r", 237, "Rejeição: CPF do destinatário inválido"), await rejecting.IssueAsync(Invoice(), Ct));

        var (pending, _) = Issuer(Api(existing: [new { id = "nfe_p", status = "pendente" }]));
        await Assert.ThrowsAsync<InvoiceIssuerUnavailableException>(() => pending.IssueAsync(Invoice(), Ct));

        var (down, _) = Issuer((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<InvoiceIssuerUnavailableException>(() => down.IssueAsync(Invoice(), Ct));
    }

    [Fact]
    public async Task Cancelamento_manda_a_justificativa_e_a_recusa_vira_erro_proprio()
    {
        var (issuer, stub) = Issuer((req, _) => req.Method == HttpMethod.Get
            ? Json(new { id = "nfe_1", status = "autorizado" })
            : Json(new { id = "evt_1", status = "registrado", codigo_status = 135 }));
        await issuer.CancelAsync("nfe_1", InvoiceCanceller.Justification, Ct);
        var cancel = stub.Requests.Single(r => r.PathAndQuery == "/nfe/nfe_1/cancelamento");
        Assert.Contains("estornado ao cliente", cancel.Body, StringComparison.Ordinal);

        var (late, _) = Issuer((req, _) => req.Method == HttpMethod.Get
            ? Json(new { id = "nfe_1", status = "autorizado" })
            : Json(new { error = new { code = "UnprocessableEntity", message = "prazo de cancelamento vencido" } }, HttpStatusCode.UnprocessableEntity));
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
        var (issuer, stub) = Issuer(Api());
        await issuer.IssueAsync(Invoice(freight: 2431), Ct);
        using var body = JsonDocument.Parse(stub.Requests.Single(r => r.Method == HttpMethod.Post && r.PathAndQuery == "/nfe").Body);
        var inf = body.RootElement.GetProperty("infNFe");
        var total = inf.GetProperty("total").GetProperty("ICMSTot");
        Assert.Equal((249.9m, 24.31m, 274.21m), (total.GetProperty("vProd").GetDecimal(), total.GetProperty("vFrete").GetDecimal(), total.GetProperty("vNF").GetDecimal()));
        var perItem = inf.GetProperty("det").EnumerateArray().Sum(d => d.GetProperty("prod").GetProperty("vFrete").GetDecimal());
        Assert.Equal(24.31m, perItem);
        Assert.Equal(274.21m, inf.GetProperty("pag").GetProperty("detPag")[0].GetProperty("vPag").GetDecimal());
    }

    [Fact]
    public async Task Leva_os_impostos_do_Simples_e_o_ICMSTot_completo()
    {
        var (issuer, stub) = Issuer(Api());
        await issuer.IssueAsync(Invoice(freight: 1000), Ct);
        var inf = Posted(stub).GetProperty("infNFe");

        var imposto = inf.GetProperty("det")[0].GetProperty("imposto");
        var icms = imposto.GetProperty("ICMS").GetProperty("ICMSSN102");
        Assert.Equal((0, 102), (icms.GetProperty("orig").GetInt32(), icms.GetProperty("CSOSN").GetInt32()));
        Assert.Equal("49", imposto.GetProperty("PIS").GetProperty("PISOutr").GetProperty("CST").GetString());
        Assert.Equal("49", imposto.GetProperty("COFINS").GetProperty("COFINSOutr").GetProperty("CST").GetString());

        var total = inf.GetProperty("total").GetProperty("ICMSTot");
        string[] fields = ["vBC", "vICMS", "vICMSDeson", "vFCP", "vBCST", "vST", "vFCPST", "vFCPSTRet", "vProd", "vFrete",
            "vSeg", "vDesc", "vII", "vIPI", "vIPIDevol", "vPIS", "vCOFINS", "vOutro", "vNF"];
        Assert.Equal(fields.Order(), total.EnumerateObject().Select(p => p.Name).Order());

        var ide = inf.GetProperty("ide");
        Assert.Equal((0, 0), (ide.GetProperty("procEmi").GetInt32(), ide.GetProperty("indIntermed").GetInt32()));
        Assert.False(string.IsNullOrEmpty(ide.GetProperty("verProc").GetString()));
        Assert.EndsWith("-03:00", ide.GetProperty("dhEmi").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Devolucao_usa_o_CSOSN_900_e_o_CST_de_entrada()
    {
        var (issuer, stub) = Issuer(Api());
        var invoice = Invoice();
        invoice.MarkAuthorized("nfe_venda", s_key, 1000, 1, DateTimeOffset.UtcNow);
        await issuer.IssueReturnAsync(invoice, Ct);
        var imposto = Posted(stub).GetProperty("infNFe").GetProperty("det")[0].GetProperty("imposto");
        Assert.Equal(900, imposto.GetProperty("ICMS").GetProperty("ICMSSN900").GetProperty("CSOSN").GetInt32());
        Assert.Equal("99", imposto.GetProperty("PIS").GetProperty("PISOutr").GetProperty("CST").GetString());
    }

    [Fact]
    public async Task Destinatario_com_o_municipio_do_CEP_e_o_nome_que_a_homologacao_pede()
    {
        var (issuer, stub) = Issuer(Api());
        await issuer.IssueAsync(Invoice(), Ct);
        Assert.Contains(stub.Requests, r => r.PathAndQuery == "/cep/01310100");
        var dest = Posted(stub).GetProperty("infNFe").GetProperty("dest");
        Assert.Equal(NuvemFiscalInvoiceIssuer.HomologationRecipient, dest.GetProperty("xNome").GetString());
        Assert.Equal("3550308", dest.GetProperty("enderDest").GetProperty("cMun").GetString());

        var (production, prodStub) = Issuer(Api(), environment: "producao");
        await production.IssueAsync(Invoice(), Ct);
        Assert.Equal("Ana Souza", Posted(prodStub).GetProperty("infNFe").GetProperty("dest").GetProperty("xNome").GetString());
    }

    [Fact]
    public async Task CEP_sem_municipio_rejeita_sem_mandar_a_nota()
    {
        var (issuer, stub) = Issuer((req, _) => req.RequestUri!.AbsolutePath.StartsWith("/cep/", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") }
            : Api()(req, ""));
        var result = Assert.IsType<InvoiceIssue.Rejected>(await issuer.IssueAsync(Invoice(), Ct));
        Assert.Contains("CEP", result.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(stub.Requests, r => r.Method == HttpMethod.Post && r.PathAndQuery == "/nfe");
    }

    [Fact]
    public async Task Cartao_leva_o_grupo_card_e_o_telefone_so_com_digitos()
    {
        var (issuer, stub) = Issuer(Api());
        var invoice = Domain.Invoice.Create(
            Guid.NewGuid(), Guid.NewGuid(), "ana@exemplo.com",
            new ShipTo("Ana Souza", "52998224725", "(11) 98765-4321", "01310-100", "Avenida Paulista", "1000", null, "Bela Vista", "São Paulo", "SP"),
            [new InvoiceLine("MOU-1", "Mouse", "84716053", 1, 4995, 4995)], "card", DateTimeOffset.UtcNow);
        await issuer.IssueAsync(invoice, Ct);
        var inf = Posted(stub).GetProperty("infNFe");
        Assert.Equal(2, inf.GetProperty("pag").GetProperty("detPag")[0].GetProperty("card").GetProperty("tpIntegra").GetInt32());
        Assert.Equal("11987654321", inf.GetProperty("dest").GetProperty("enderDest").GetProperty("fone").GetString());
        Assert.Contains(stub.Requests, r => r.PathAndQuery == "/cep/01310100");

        var (pix, pixStub) = Issuer(Api());
        await pix.IssueAsync(Invoice(payment: "pix"), Ct);
        Assert.False(Posted(pixStub).GetProperty("infNFe").GetProperty("pag").GetProperty("detPag")[0].TryGetProperty("card", out _));
    }

    [Theory]
    [InlineData("denegado")]
    [InlineData("erro")]
    public async Task Denegada_ou_com_erro_e_rejeicao_e_nao_tentativa_sem_fim(string status)
    {
        var (issuer, _) = Issuer(Api(() => Json(new { id = "nfe_x", status, autorizacao = new { codigo_status = 302, motivo_status = "Uso Denegado" } })));
        Assert.Equal(new InvoiceIssue.Rejected("nfe_x", 302, "Uso Denegado"), await issuer.IssueAsync(Invoice(), Ct));
    }

    [Fact]
    public async Task Corpo_recusado_pelo_emissor_e_rejeicao()
    {
        var (issuer, _) = Issuer(Api(() => Json(new { error = new { code = "ValidationFailed", message = "infNFe.det[0].imposto obrigatório" } }, HttpStatusCode.BadRequest)));
        var result = Assert.IsType<InvoiceIssue.Rejected>(await issuer.IssueAsync(Invoice(), Ct));
        Assert.Contains("imposto", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelamento_rejeitado_pela_SEFAZ_ou_pendente_e_ja_cancelada()
    {
        var (rejected, _) = Issuer((req, _) => req.Method == HttpMethod.Get
            ? Json(new { id = "nfe_1", status = "autorizado" })
            : Json(new { id = "evt_1", status = "rejeitado", codigo_status = 501, motivo_status = "Rejeição: Prazo de cancelamento superior ao previsto" }));
        var e = await Assert.ThrowsAsync<InvoiceCancelRefusedException>(() => rejected.CancelAsync("nfe_1", InvoiceCanceller.Justification, Ct));
        Assert.Contains("501", e.Message, StringComparison.Ordinal);

        var (pending, _) = Issuer((req, _) => req.Method == HttpMethod.Get
            ? Json(new { id = "nfe_1", status = "autorizado" })
            : Json(new { id = "evt_1", status = "pendente" }));
        await Assert.ThrowsAsync<InvoiceIssuerUnavailableException>(() => pending.CancelAsync("nfe_1", InvoiceCanceller.Justification, Ct));

        var (done, stub) = Issuer((_, _) => Json(new { id = "nfe_1", status = "cancelado" }));
        await done.CancelAsync("nfe_1", InvoiceCanceller.Justification, Ct);
        Assert.DoesNotContain(stub.Requests, r => r.Method == HttpMethod.Post && r.PathAndQuery.EndsWith("/cancelamento", StringComparison.Ordinal));
    }
}
