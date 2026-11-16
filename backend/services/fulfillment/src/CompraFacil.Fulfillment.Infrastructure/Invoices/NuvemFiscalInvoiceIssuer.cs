using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CompraFacil.Fulfillment.Application.Invoices;
using CompraFacil.Fulfillment.Domain;
using Microsoft.Extensions.Options;

namespace CompraFacil.Fulfillment.Infrastructure.Invoices;

/// <summary>
/// A NF-e pela API da Nuvem Fiscal (em dev, o invoice-sim no mesmo contrato). A referência da nota é o
/// order_id: antes de emitir, procura a nota do pedido (uma tentativa anterior pode ter emitido e caído antes
/// da resposta), e só emite se não houver. O corpo leva as tags da NF-e que a loja preenche, com os impostos
/// do Simples Nacional sem destaque (CSOSN e CST da configuração, que o contador confirma) e o município do
/// destinatário pelo CEP, na consulta de CEP da própria Nuvem Fiscal.
/// </summary>
internal sealed class NuvemFiscalInvoiceIssuer(HttpClient http, NuvemFiscalTokenCache tokens, IOptions<InvoiceOptions> options, TimeProvider clock)
    : IInvoiceIssuer
{
    private const string Authorized = "autorizado";
    private const string Rejected = "rejeitado";
    private const string Cancelled = "cancelado";

    // denegado: a SEFAZ negou o uso (o número fica gasto); erro: a Nuvem Fiscal recusou antes da SEFAZ
    private const string Denied = "denegado";
    private const string Failed = "erro";

    // o evento de cancelamento: registrado na SEFAZ, rejeitado por ela ou ainda pendente
    private const string EventRegistered = "registrado";
    private const string EventRejected = "rejeitado";

    /// <summary>Na homologação, a SEFAZ só aceita este nome no destinatário (rejeição 598).</summary>
    internal const string HomologationRecipient = "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

    private static readonly TimeSpan s_brasilia = TimeSpan.FromHours(-3);

    // forma de pagamento da NF-e (tPag): 17 = PIX, 03 = cartão de crédito
    private static readonly Dictionary<string, string> s_paymentCodes = new(StringComparer.OrdinalIgnoreCase) { ["pix"] = "17", ["card"] = "03" };

    public Task<InvoiceIssue> IssueAsync(Invoice invoice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return EmitAsync(invoice, invoice.Id.ToString(), isReturn: false, ct);
    }

    public Task<InvoiceIssue> IssueReturnAsync(Invoice invoice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return EmitAsync(invoice, ReturnReference(invoice), isReturn: true, ct);
    }

    /// <summary>A devolução tem a própria referência: a do pedido é a nota de venda.</summary>
    private static string ReturnReference(Invoice invoice) => $"{invoice.Id}-devolucao";

    private async Task<InvoiceIssue> EmitAsync(Invoice invoice, string reference, bool isReturn, CancellationToken ct)
    {
        var o = options.Value;
        // a consulta pede o CNPJ do emitente: a mesma conta pode emitir por mais de uma empresa
        var found = await SendAsync<Page>(() => new HttpRequestMessage(HttpMethod.Get,
            $"nfe?cpf_cnpj={Uri.EscapeDataString(o.Issuer.Cnpj)}&ambiente={Uri.EscapeDataString(o.Environment)}&referencia={Uri.EscapeDataString(reference)}"), ct);
        var dfe = found?.Data is { Count: > 0 } list ? list[0] : null;
        if (dfe is null)
        {
            if (await CityCodeAsync(invoice.ShipTo.ZipCode, ct) is not { } cityCode)
            {
                // sem o município, a SEFAZ rejeitaria a nota (cMun é obrigatório): nem adianta mandar
                return new InvoiceIssue.Rejected(null, 0, $"CEP do destinatário sem município na consulta de CEP: {invoice.ShipTo.ZipCode}");
            }
            var body = Body(invoice, o, cityCode, isReturn);
            try
            {
                dfe = await SendAsync<Dfe>(() => new HttpRequestMessage(HttpMethod.Post, "nfe")
                {
                    Content = JsonContent.Create(body, options: Json.Write),
                }, ct) ?? throw new InvoiceIssuerUnavailableException("resposta vazia do emissor");
            }
            catch (HttpStatusException e) when (e.Status is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
            {
                // a Nuvem Fiscal recusou o corpo antes da SEFAZ (schema): tentar de novo dá no mesmo
                return new InvoiceIssue.Rejected(null, 0, $"o emissor recusou a nota: {e.Body}");
            }
        }
        return dfe.Status switch
        {
            Authorized or Cancelled when dfe.Chave is { Length: 44 } key && dfe.Numero is { } number =>
                new InvoiceIssue.Authorized(dfe.Id, key, number, dfe.Serie ?? o.Series, dfe.Autorizacao?.DataEvento ?? clock.GetUtcNow()),
            Rejected or Denied or Failed => new InvoiceIssue.Rejected(dfe.Id, dfe.Autorizacao?.CodigoStatus ?? 0,
                dfe.Autorizacao?.MotivoStatus ?? dfe.Autorizacao?.Mensagem ?? $"nota {dfe.Status} no emissor"),
            // pendente ou em processamento na SEFAZ: a próxima tentativa acha a nota pela referência
            _ => throw new InvoiceIssuerUnavailableException($"nota ainda {dfe.Status} no emissor"),
        };
    }

    /// <summary>O código IBGE do município pelo CEP (GET /cep). Nulo se o CEP não existe na consulta.</summary>
    private async Task<string?> CityCodeAsync(string zipCode, CancellationToken ct)
    {
        var cep = new string([.. zipCode.Where(char.IsAsciiDigit)]);
        try
        {
            var found = await SendAsync<ZipCodeAddress>(() => new HttpRequestMessage(HttpMethod.Get, $"cep/{cep}"), ct);
            return found?.CodigoIbge is { Length: 7 } code && code.All(char.IsAsciiDigit) ? code : null;
        }
        catch (HttpStatusException e) when (e.Status is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }
    }

    public async Task<string> GetXmlAsync(string providerId, CancellationToken ct)
    {
        using var response = await SendRawAsync(() => new HttpRequestMessage(HttpMethod.Get, $"nfe/{Uri.EscapeDataString(providerId)}/xml"), ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<byte[]> GetPdfAsync(string providerId, CancellationToken ct)
    {
        using var response = await SendRawAsync(() => new HttpRequestMessage(HttpMethod.Get, $"nfe/{Uri.EscapeDataString(providerId)}/pdf"), ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task CancelAsync(string providerId, string justification, CancellationToken ct)
    {
        var id = Uri.EscapeDataString(providerId);
        // uma tentativa anterior pode ter cancelado e caído antes da resposta: pedir de novo seria recusado
        if ((await SendAsync<Dfe>(() => new HttpRequestMessage(HttpMethod.Get, $"nfe/{id}"), ct))?.Status == Cancelled)
        {
            return;
        }
        CancelEvent? cancelled;
        try
        {
            cancelled = await SendAsync<CancelEvent>(() => new HttpRequestMessage(HttpMethod.Post, $"nfe/{id}/cancelamento")
            {
                Content = JsonContent.Create(new { justificativa = justification }, options: Json.Write),
            }, ct);
        }
        catch (HttpStatusException e) when (e.Status is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest)
        {
            throw new InvoiceCancelRefusedException($"o emissor recusou o cancelamento: {e.Body}");
        }
        switch (cancelled?.Status)
        {
            case EventRegistered:
                return;
            case EventRejected:
                // a SEFAZ recusou o evento (fora do prazo, nota já com outro evento...): a equipe resolve
                throw new InvoiceCancelRefusedException(
                    $"a SEFAZ recusou o cancelamento ({cancelled.CodigoStatus}): {cancelled.MotivoStatus ?? cancelled.Mensagem}");
            default:
                // pendente: na próxima volta, a consulta da nota diz se cancelou
                throw new InvoiceIssuerUnavailableException($"cancelamento ainda {cancelled?.Status ?? "sem resposta"} no emissor");
        }
    }

    private object Body(Invoice invoice, InvoiceOptions o, string recipientCityCode, bool isReturn)
    {
        var to = invoice.ShipTo;
        var issuer = o.Issuer;
        var sameState = string.Equals(to.State, issuer.State, StringComparison.OrdinalIgnoreCase);
        var cfop = isReturn
            ? (sameState ? o.ReturnCfopInState : o.ReturnCfopOutOfState)
            : (sameState ? o.CfopInState : o.CfopOutOfState);
        decimal Reais(long cents) => cents / 100m;
        var products = invoice.Lines.Sum(l => l.TotalCents);
        var freight = Freight(invoice);
        var csosn = isReturn ? o.ReturnCsosn : o.Csosn;
        var pisCofins = isReturn ? o.ReturnPisCofinsCst : o.PisCofinsCst;
        var homologation = string.Equals(o.Environment, "homologacao", StringComparison.OrdinalIgnoreCase);
        var phone = new string([.. to.Phone.Where(char.IsAsciiDigit)]);
        return new
        {
            ambiente = o.Environment,
            referencia = isReturn ? ReturnReference(invoice) : invoice.Id.ToString(),
            infNFe = new
            {
                versao = "4.00",
                ide = new
                {
                    cUF = StateCode(issuer.State),
                    natOp = isReturn ? o.ReturnOperationNature : o.OperationNature,
                    mod = 55,
                    serie = o.Series,
                    // no horário de Brasília (sem horário de verão desde 2019): a SEFAZ confere o fuso da UF
                    dhEmi = clock.GetUtcNow().ToOffset(s_brasilia).ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture),
                    tpNF = isReturn ? 0 : 1, // entrada na devolução, saída na venda
                    idDest = sameState ? 1 : 2, // operação interna ou interestadual
                    cMunFG = issuer.CityCode,
                    tpImp = 1,
                    tpEmis = 1,
                    finNFe = isReturn ? 4 : 1, // devolução ou normal
                    NFref = isReturn ? new[] { new { refNFe = invoice.AccessKey } } : null, // a nota de venda
                    indFinal = 1, // consumidor final
                    indPres = 2, // venda pela internet
                    indIntermed = 0, // no site da própria loja, sem marketplace (obrigatório com indPres=2)
                    procEmi = 0, // aplicativo do contribuinte
                    verProc = o.AppVersion,
                },
                emit = new
                {
                    CNPJ = issuer.Cnpj,
                    xNome = issuer.Name,
                    enderEmit = new
                    {
                        xLgr = issuer.Street,
                        nro = issuer.Number,
                        xBairro = issuer.District,
                        cMun = issuer.CityCode,
                        xMun = issuer.City,
                        UF = issuer.State,
                        CEP = issuer.ZipCode,
                        cPais = "1058",
                        xPais = "BRASIL",
                    },
                    IE = issuer.StateRegistration,
                    CRT = issuer.TaxRegime,
                },
                dest = new
                {
                    CPF = to.Cpf,
                    xNome = homologation ? HomologationRecipient : to.Name,
                    enderDest = new
                    {
                        xLgr = to.Street,
                        nro = to.Number,
                        xCpl = to.Complement,
                        xBairro = to.District,
                        cMun = recipientCityCode,
                        xMun = to.City,
                        UF = to.State,
                        CEP = to.ZipCode,
                        cPais = "1058",
                        xPais = "BRASIL",
                        fone = phone.Length is >= 6 and <= 14 ? phone : null, // só dígitos, com o DDD
                    },
                    indIEDest = 9, // não contribuinte
                    email = invoice.CustomerEmail,
                },
                det = invoice.Lines.Select((l, i) => new
                {
                    nItem = i + 1,
                    prod = new
                    {
                        cProd = l.Sku,
                        cEAN = "SEM GTIN",
                        xProd = l.Name,
                        NCM = l.Ncm,
                        CFOP = cfop,
                        uCom = "UN",
                        qCom = l.Quantity,
                        vUnCom = Reais(l.UnitPriceCents),
                        vProd = Reais(l.TotalCents),
                        cEANTrib = "SEM GTIN",
                        uTrib = "UN",
                        qTrib = l.Quantity,
                        vUnTrib = Reais(l.UnitPriceCents),
                        indTot = 1,
                        vFrete = freight[i] > 0 ? Reais(freight[i]) : (decimal?)null,
                    },
                    // Simples Nacional: o ICMS, o PIS e a COFINS são recolhidos no DAS, sem destaque na nota
                    imposto = new
                    {
                        ICMS = csosn == 900
                            ? (object)new { ICMSSN900 = new { orig = o.ProductOrigin, CSOSN = csosn } }
                            : new { ICMSSN102 = new { orig = o.ProductOrigin, CSOSN = csosn } },
                        PIS = new { PISOutr = new { CST = pisCofins, vBC = 0m, pPIS = 0m, vPIS = 0m } },
                        COFINS = new { COFINSOutr = new { CST = pisCofins, vBC = 0m, pCOFINS = 0m, vCOFINS = 0m } },
                    },
                }).ToList(),
                // o total: os produtos, o frete cobrado do cliente (#222) e a nota, que soma os dois. O
                // modelo pede todos os campos do ICMSTot, zerados onde a loja não tem valor
                total = new
                {
                    ICMSTot = new
                    {
                        vBC = 0m,
                        vICMS = 0m,
                        vICMSDeson = 0m,
                        vFCP = 0m,
                        vBCST = 0m,
                        vST = 0m,
                        vFCPST = 0m,
                        vFCPSTRet = 0m,
                        vProd = Reais(products),
                        vFrete = Reais(invoice.FreightCents),
                        vSeg = 0m,
                        vDesc = 0m,
                        vII = 0m,
                        vIPI = 0m,
                        vIPIDevol = 0m,
                        vPIS = 0m,
                        vCOFINS = 0m,
                        vOutro = 0m,
                        vNF = Reais(invoice.TotalCents),
                    },
                },
                transp = new { modFrete = 0 }, // frete por conta do emitente (o fornecedor manda)
                pag = new
                {
                    // a devolução não recebe nada (90 = sem pagamento): o estorno ao cliente é fora da nota
                    detPag = new[] { Payment(invoice, isReturn) },
                },
            },
        };
    }

    private static object Payment(Invoice invoice, bool isReturn)
    {
        if (isReturn)
        {
            return new { tPag = "90", vPag = 0m };
        }
        var code = s_paymentCodes.GetValueOrDefault(invoice.PaymentMethod, "99");
        var paid = invoice.TotalCents / 100m;
        // cartão (03) pede o grupo card; 2 = pagamento não integrado à nota (a loja não manda bandeira nem autorização)
        return code == "03"
            ? new { tPag = code, vPag = paid, card = (object?)new { tpIntegra = 2 } }
            : new { tPag = code, vPag = paid, card = (object?)null };
    }

    /// <summary>O frete rateado entre os itens pelo valor de cada um (a NF-e pede o vFrete por item); o último leva a sobra.</summary>
    private static long[] Freight(Invoice invoice)
    {
        var lines = invoice.Lines;
        var products = lines.Sum(l => l.TotalCents);
        var shares = new long[lines.Count];
        long given = 0;
        for (var i = 0; i < lines.Count - 1; i++)
        {
            shares[i] = products == 0 ? 0 : invoice.FreightCents * lines[i].TotalCents / products;
            given += shares[i];
        }
        shares[^1] = invoice.FreightCents - given;
        return shares;
    }

    private static readonly Dictionary<string, int> s_stateCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RO"] = 11,
        ["AC"] = 12,
        ["AM"] = 13,
        ["RR"] = 14,
        ["PA"] = 15,
        ["AP"] = 16,
        ["TO"] = 17,
        ["MA"] = 21,
        ["PI"] = 22,
        ["CE"] = 23,
        ["RN"] = 24,
        ["PB"] = 25,
        ["PE"] = 26,
        ["AL"] = 27,
        ["SE"] = 28,
        ["BA"] = 29,
        ["MG"] = 31,
        ["ES"] = 32,
        ["RJ"] = 33,
        ["SP"] = 35,
        ["PR"] = 41,
        ["SC"] = 42,
        ["RS"] = 43,
        ["MS"] = 50,
        ["MT"] = 51,
        ["GO"] = 52,
        ["DF"] = 53,
    };

    private static int StateCode(string state) => s_stateCodes.TryGetValue(state, out var code)
        ? code
        : throw new InvalidOperationException($"UF do emitente inválida: {state}");

    private async Task<T?> SendAsync<T>(Func<HttpRequestMessage> request, CancellationToken ct)
    {
        using var response = await SendRawAsync(request, ct);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json.Read, ct);
        }
        catch (JsonException e)
        {
            throw new InvoiceIssuerUnavailableException("resposta do emissor fora do formato", e);
        }
    }

    /// <summary>A chamada com o token; 401 renova o token uma vez. 5xx e falha de rede: fora do ar; 4xx: erro do pedido.</summary>
    private async Task<HttpResponseMessage> SendRawAsync(Func<HttpRequestMessage> request, CancellationToken ct, bool retried = false)
    {
        HttpResponseMessage response;
        try
        {
            using var message = request();
            message.Headers.Authorization = new("Bearer", await tokens.GetAsync(ct));
            response = await http.SendAsync(message, ct);
        }
        catch (HttpRequestException e)
        {
            throw new InvoiceIssuerUnavailableException($"emissor inacessível: {e.Message}", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new InvoiceIssuerUnavailableException("o emissor não respondeu a tempo", e);
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized && !retried)
        {
            response.Dispose();
            tokens.Invalidate();
            return await SendRawAsync(request, ct, retried: true);
        }
        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new InvoiceIssuerUnavailableException($"emissor respondeu {status}");
        }
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpStatusException(status, body);
        }
        return response;
    }

    private sealed class HttpStatusException(HttpStatusCode status, string body) : Exception($"emissor respondeu {(int)status}: {body}")
    {
        public HttpStatusCode Status { get; } = status;
        public string Body { get; } = body;
    }

    private sealed record Page(IReadOnlyList<Dfe>? Data);

    private sealed record Dfe(string Id, string Status, string? Chave, int? Numero, int? Serie, Authorization? Autorizacao);

    private sealed record Authorization(int CodigoStatus, string? MotivoStatus, string? Mensagem, DateTimeOffset? DataEvento);

    private sealed record CancelEvent(string? Id, string? Status, int? CodigoStatus, string? MotivoStatus, string? Mensagem);

    private sealed record ZipCodeAddress(string? CodigoIbge, string? Municipio, string? Uf);
}

/// <summary>O token OAuth2 do emissor, guardado até perto de vencer (um por processo).</summary>
internal sealed class NuvemFiscalTokenCache(IHttpClientFactory factory, IOptions<InvoiceOptions> options, TimeProvider clock) : IDisposable
{
    public const string ClientName = "nuvem-fiscal-auth";
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetAsync(CancellationToken ct)
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
            var o = options.Value;
            using var http = factory.CreateClient(ClientName);
            using var response = await http.PostAsync(o.AuthUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = o.ClientId,
                ["client_secret"] = o.ClientSecret,
                ["scope"] = o.Scope,
            }), ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvoiceIssuerUnavailableException($"o emissor não deu o token ({(int)response.StatusCode})");
            }
            var body = await response.Content.ReadFromJsonAsync<TokenResult>(Json.Read, ct)
                ?? throw new InvoiceIssuerUnavailableException("o emissor deu um token vazio");
            _token = body.AccessToken;
            // renova com folga: na metade do prazo, até 5 minutos antes
            var lifetime = TimeSpan.FromSeconds(Math.Max(body.ExpiresIn, 60));
            _expiresAt = clock.GetUtcNow() + lifetime - TimeSpan.FromTicks(Math.Min(TimeSpan.FromMinutes(5).Ticks, lifetime.Ticks / 2));
            return _token;
        }
        catch (HttpRequestException e)
        {
            throw new InvoiceIssuerUnavailableException($"emissor inacessível: {e.Message}", e);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate() => _token = null;

    public void Dispose() => _lock.Dispose();

    private sealed record TokenResult(string AccessToken, int ExpiresIn);
}

internal static class Json
{
    /// <summary>O corpo da emissão leva as tags da NF-e como estão (infNFe, CNPJ, xNome...).</summary>
    public static readonly JsonSerializerOptions Write = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>As respostas da Nuvem Fiscal (e do OAuth) vêm em snake_case.</summary>
    public static readonly JsonSerializerOptions Read = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}
