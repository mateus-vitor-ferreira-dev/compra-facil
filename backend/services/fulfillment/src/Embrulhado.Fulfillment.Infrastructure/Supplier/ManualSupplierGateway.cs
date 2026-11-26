using Embrulhado.Fulfillment.Application.ManualSupplier;
using Embrulhado.Fulfillment.Application.SupplierOrders;
using Embrulhado.Fulfillment.Domain;
using Microsoft.Extensions.Options;

namespace Embrulhado.Fulfillment.Infrastructure.Supplier;

/// <summary>Um serviço de envio da tabela da loja: o preço até 1 kg, o de cada quilo a mais e o prazo da transportadora.</summary>
public sealed class ManualFreightService
{
    /// <summary>O nome do serviço, que <c>Shipping:Services</c> traduz para o cliente (manual-economico, manual-expresso).</summary>
    public string Service { get; set; } = "";
    public long BaseCents { get; set; }
    public long PerExtraKgCents { get; set; }
    public int MinDays { get; set; }
    public int MaxDays { get; set; }
}

/// <summary>
/// Seção Supplier:Manual: o fornecedor sem API (#412). A tabela de frete é da loja, em reais, até haver a do
/// fornecedor escolhido; os padrões são um ponto de partida, a ajustar com a primeira cotação real.
/// </summary>
public sealed class ManualSupplierOptions
{
    public const string Section = "Supplier:Manual";

    /// <summary>Unidades que a loja não vende do estoque informado: a planilha envelhece entre uma subida e outra.</summary>
    public int SafetyStock { get; set; } = 1;

    /// <summary>Dias úteis que o fornecedor leva para despachar, somados ao prazo da transportadora.</summary>
    public int HandlingDays { get; set; } = 2;

    /// <summary>O peso do produto sem peso na planilha.</summary>
    public int DefaultWeightGrams { get; set; } = 500;

    /// <summary>Os serviços de envio. Vazio: os padrões (econômico e expresso).</summary>
    public List<ManualFreightService> Freight { get; set; } = [];

    /// <summary>
    /// Acréscimo no frete, em porcentagem, pelo primeiro dígito do CEP (a região dos Correios: 0 e 1 SP, 2 RJ e ES,
    /// 3 MG, 4 BA e SE, 5 PE, AL, PB e RN, 6 Norte e parte do Nordeste, 7 Centro-Oeste, 8 PR e SC, 9 RS).
    /// </summary>
    public Dictionary<string, int> RegionPercent { get; set; } = new()
    {
        ["0"] = 0,
        ["1"] = 0,
        ["2"] = 10,
        ["3"] = 0,
        ["4"] = 25,
        ["5"] = 30,
        ["6"] = 45,
        ["7"] = 25,
        ["8"] = 10,
        ["9"] = 15,
    };

    /// <summary>Dias úteis a mais no prazo, pelo primeiro dígito do CEP.</summary>
    public Dictionary<string, int> RegionExtraDays { get; set; } = new()
    {
        ["0"] = 0,
        ["1"] = 0,
        ["2"] = 1,
        ["3"] = 0,
        ["4"] = 3,
        ["5"] = 4,
        ["6"] = 6,
        ["7"] = 3,
        ["8"] = 2,
        ["9"] = 3,
    };

    internal static readonly ManualFreightService[] DefaultFreight =
    [
        new() { Service = "manual-economico", BaseCents = 1990, PerExtraKgCents = 450, MinDays = 4, MaxDays = 8 },
        new() { Service = "manual-expresso", BaseCents = 3490, PerExtraKgCents = 890, MinDays = 1, MaxDays = 3 },
    ];
}

/// <summary>
/// O fornecedor manual (#412, ADR 0007): sem API, a equipe faz o pedido no fornecedor pelo painel. O feed e o
/// estoque são a última planilha subida; o frete, a tabela da loja; o pedido pago vai para a fila "Pedidos a fazer".
/// </summary>
internal sealed class ManualSupplierGateway(IManualCatalogRepository sheet, IOptions<ManualSupplierOptions> options) : ISupplierGateway
{
    public string Name => ManualSupplierMode.SupplierName;

    public Task<SupplierPlacement> PlaceOrderAsync(SupplierOrderRequest request, CancellationToken ct) =>
        Task.FromResult<SupplierPlacement>(new SupplierPlacement.AwaitingStaff());

    /// <summary>A devolução é combinada com o fornecedor pela equipe; o painel dela vem com a #412.</summary>
    public Task<SupplierReturnOpening> OpenReturnAsync(
        Guid orderNumber, string customerEmail, IReadOnlyList<SupplierOrderItem> items, CancellationToken ct) =>
        Task.FromResult<SupplierReturnOpening>(new SupplierReturnOpening.Refused(
            "fornecedor manual: a equipe combina a devolução com o fornecedor"));

    /// <summary>
    /// Feito pela equipe no fornecedor, o pedido só ela cancela lá: a loja não estorna o cliente sem isso. Antes de
    /// feito (na fila), o cancelamento não chega aqui.
    /// </summary>
    public Task<bool> CancelOrderAsync(string supplierOrderId, CancellationToken ct) => Task.FromResult(false);

    public Task CheckAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<IReadOnlyList<SupplierFeedItem>> ListProductsAsync(CancellationToken ct) =>
        [.. (await sheet.ListAsync(ct)).Select(i => i.ToFeedItem())];

    public async Task<IReadOnlyDictionary<string, int>> GetStockAsync(IReadOnlyCollection<string> vids, CancellationToken ct)
    {
        var safety = Math.Max(0, options.Value.SafetyStock);
        var stock = (await sheet.ListAsync(ct)).ToDictionary(i => i.Sku, i => i.Stock, StringComparer.OrdinalIgnoreCase);
        return vids.Distinct().ToDictionary(v => v, v => Math.Max(0, stock.GetValueOrDefault(v) - safety));
    }

    public async Task<IReadOnlyList<SupplierFreightOption>> QuoteFreightAsync(string zipCode, IReadOnlyList<SupplierOrderItem> items, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(zipCode);
        ArgumentNullException.ThrowIfNull(items);
        var o = options.Value;
        var weights = (await sheet.ListAsync(ct)).ToDictionary(i => i.Sku, i => i.WeightGrams, StringComparer.OrdinalIgnoreCase);
        var grams = items.Sum(i => (long)(weights.GetValueOrDefault(i.Sku) ?? o.DefaultWeightGrams) * i.Quantity);
        return [.. Quote(zipCode, grams, o)];
    }

    /// <summary>O frete da tabela: o preço até 1 kg, mais cada quilo começado depois dele, com o acréscimo da região.</summary>
    internal static IEnumerable<SupplierFreightOption> Quote(string zipCode, long grams, ManualSupplierOptions o)
    {
        var region = zipCode.FirstOrDefault(char.IsAsciiDigit).ToString();
        var percent = o.RegionPercent.GetValueOrDefault(region);
        var extraDays = o.RegionExtraDays.GetValueOrDefault(region);
        var extraKg = Math.Max(0, (grams + 999) / 1000 - 1);
        foreach (var s in o.Freight.Count > 0 ? o.Freight : [.. ManualSupplierOptions.DefaultFreight])
        {
            var cents = s.BaseCents + (s.PerExtraKgCents * extraKg);
            cents += (cents * percent + 99) / 100;
            yield return new SupplierFreightOption(s.Service, cents, "BRL",
                s.MinDays + o.HandlingDays + extraDays, Math.Max(s.MinDays, s.MaxDays) + o.HandlingDays + extraDays);
        }
    }
}
