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
/// da resposta), e só emite se não houver. O corpo leva as tags da NF-e que a loja preenche; os impostos
/// (Simples Nacional, sem destaque) entram com a venda de verdade, conferidos pelo contador.
/// </summary>
internal sealed class NuvemFiscalInvoiceIssuer(HttpClient http, NuvemFiscalTokenCache tokens, IOptions<InvoiceOptions> options, TimeProvider clock)
    : IInvoiceIssuer
{
    private const string Authorized = "autorizado";
    private const string Rejected = "rejeitado";
    private const string Cancelled = "cancelado";

    // forma de pagamento da NF-e (tPag): 17 = PIX, 03 = cartão de crédito
    private static readonly Dictionary<string, string> s_paymentCodes = new(StringComparer.OrdinalIgnoreCase) { ["pix"] = "17", ["card"] = "03" };

    public async Task<InvoiceIssue> IssueAsync(Invoice invoice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var o = options.Value;
        var reference = invoice.Id.ToString();
        var found = await SendAsync<Page>(() => new HttpRequestMessage(HttpMethod.Get,
            $"nfe?ambiente={Uri.EscapeDataString(o.Environment)}&referencia={Uri.EscapeDataString(reference)}"), ct);
        var existing = found?.Data is { Count: > 0 } list ? list[0] : null;
        var dfe = existing ?? await SendAsync<Dfe>(() => new HttpRequestMessage(HttpMethod.Post, "nfe")
        {
            Content = JsonContent.Create(Body(invoice, o), options: Json.Write),
        }, ct) ?? throw new InvoiceIssuerUnavailableException("resposta vazia do emissor");
        return dfe.Status switch
        {
            Authorized or Cancelled when dfe.Chave is { Length: 44 } key && dfe.Numero is { } number =>
                new InvoiceIssue.Authorized(dfe.Id, key, number, dfe.Serie ?? o.Series, dfe.Autorizacao?.DataEvento ?? clock.GetUtcNow()),
            Rejected => new InvoiceIssue.Rejected(dfe.Id, dfe.Autorizacao?.CodigoStatus ?? 0, dfe.Autorizacao?.MotivoStatus ?? "rejeitada"),
            // pendente ou em processamento na SEFAZ: a próxima tentativa acha a nota pela referência
            _ => throw new InvoiceIssuerUnavailableException($"nota ainda {dfe.Status} no emissor"),
        };
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
        try
        {
            using var response = await SendRawAsync(() => new HttpRequestMessage(HttpMethod.Post, $"nfe/{Uri.EscapeDataString(providerId)}/cancelamento")
            {
                Content = JsonContent.Create(new { justificativa = justification }, options: Json.Write),
            }, ct);
        }
        catch (HttpStatusException e) when (e.Status is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest)
        {
            throw new InvoiceCancelRefusedException($"o emissor recusou o cancelamento: {e.Body}");
        }
    }

    private object Body(Invoice invoice, InvoiceOptions o)
    {
        var to = invoice.ShipTo;
        var issuer = o.Issuer;
        var sameState = string.Equals(to.State, issuer.State, StringComparison.OrdinalIgnoreCase);
        var cfop = sameState ? o.CfopInState : o.CfopOutOfState;
        decimal Reais(long cents) => cents / 100m;
        var products = invoice.Lines.Sum(l => l.TotalCents);
        var freight = Freight(invoice);
        return new
        {
            ambiente = o.Environment,
            referencia = invoice.Id.ToString(),
            infNFe = new
            {
                versao = "4.00",
                ide = new
                {
                    cUF = StateCode(issuer.State),
                    natOp = o.OperationNature,
                    mod = 55,
                    serie = o.Series,
                    dhEmi = clock.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture),
                    tpNF = 1, // saída
                    idDest = sameState ? 1 : 2, // operação interna ou interestadual
                    cMunFG = issuer.CityCode,
                    tpImp = 1,
                    tpEmis = 1,
                    finNFe = 1, // normal
                    indFinal = 1, // consumidor final
                    indPres = 2, // venda pela internet
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
                    xNome = to.Name,
                    enderDest = new
                    {
                        xLgr = to.Street,
                        nro = to.Number,
                        xCpl = to.Complement,
                        xBairro = to.District,
                        xMun = to.City,
                        UF = to.State,
                        CEP = to.ZipCode,
                        cPais = "1058",
                        xPais = "BRASIL",
                        fone = to.Phone,
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
                }).ToList(),
                // o total: os produtos, o frete cobrado do cliente (#222) e a nota, que soma os dois
                total = new { ICMSTot = new { vProd = Reais(products), vFrete = Reais(invoice.FreightCents), vNF = Reais(invoice.TotalCents) } },
                transp = new { modFrete = 0 }, // frete por conta do emitente (o fornecedor manda)
                pag = new
                {
                    detPag = new[] { new { tPag = s_paymentCodes.GetValueOrDefault(invoice.PaymentMethod, "99"), vPag = Reais(invoice.TotalCents) } },
                },
            },
        };
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

    private sealed record Authorization(int CodigoStatus, string? MotivoStatus, DateTimeOffset? DataEvento);
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
