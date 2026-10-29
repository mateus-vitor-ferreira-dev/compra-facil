namespace CompraFacil.Fulfillment.Infrastructure.Invoices;

/// <summary>
/// Seção Invoice: o emissor de NF-e (ADR 0007, #221). Em dev, o invoice-sim do compose, no contrato da Nuvem
/// Fiscal; na venda de verdade, a Nuvem Fiscal (auth.nuvemfiscal.com.br e api.nuvemfiscal.com.br) com o CNPJ
/// da loja. Os dados do emitente e os CFOPs ficam aqui; o contador confirma antes de produção.
/// </summary>
public sealed class InvoiceOptions
{
    /// <summary>O token (OAuth2 client_credentials).</summary>
    public Uri AuthUrl { get; set; } = new("http://localhost:8094/oauth/token");

    /// <summary>Raiz da API, com a barra no fim.</summary>
    public Uri BaseUrl { get; set; } = new("http://localhost:8094/");

    public string ClientId { get; set; } = "sim-client";
    public string ClientSecret { get; set; } = "sim-secret";
    public string Scope { get; set; } = "nfe";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>homologacao (sem valor fiscal) ou producao.</summary>
    public string Environment { get; set; } = "homologacao";

    public int Series { get; set; } = 1;
    public string OperationNature { get; set; } = "Venda de mercadoria adquirida de terceiros, remetida pelo fornecedor";

    /// <summary>Venda à ordem: 5.120 para o mesmo estado da loja, 6.120 para outro (ADR 0007).</summary>
    public string CfopInState { get; set; } = "5120";
    public string CfopOutOfState { get; set; } = "6120";

    public InvoiceIssuerParty Issuer { get; set; } = new();
}

/// <summary>A loja como emitente. O padrão é um CNPJ de exemplo, válido nos dígitos verificadores.</summary>
public sealed class InvoiceIssuerParty
{
    public string Cnpj { get; set; } = "11222333000181";
    public string Name { get; set; } = "CompraFacil Comercio LTDA";
    public string StateRegistration { get; set; } = "ISENTO";

    /// <summary>Regime tributário: 1 = Simples Nacional (MEI e ME).</summary>
    public int TaxRegime { get; set; } = 1;
    public string Street { get; set; } = "Rua da Loja";
    public string Number { get; set; } = "1";
    public string District { get; set; } = "Centro";
    public string City { get; set; } = "Lavras";

    /// <summary>Código IBGE do município.</summary>
    public string CityCode { get; set; } = "3138203";
    public string State { get; set; } = "MG";
    public string ZipCode { get; set; } = "37200000";
}
