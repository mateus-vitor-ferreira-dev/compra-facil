using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Infrastructure.Supplier;

namespace CompraFacil.Fulfillment.IntegrationTests;

/// <summary>A tabela de frete do fornecedor manual (#412): base até 1 kg, quilo começado depois dele e o acréscimo da região.</summary>
public sealed class ManualFreightTests
{
    private static readonly ManualSupplierOptions s_options = new()
    {
        HandlingDays = 2,
        Freight = [new ManualFreightService { Service = "manual-economico", BaseCents = 2000, PerExtraKgCents = 500, MinDays = 4, MaxDays = 8 }],
    };

    [Theory]
    [InlineData("01310100", 1000, 2000L, 6, 10)] // SP, até 1 kg
    [InlineData("01310100", 1001, 2500L, 6, 10)] // 1 kg e 1 g: um quilo começado a mais
    [InlineData("01310100", 3000, 3000L, 6, 10)]
    [InlineData("69005040", 500, 2900L, 12, 16)] // Manaus (6): +45% e 6 dias a mais
    [InlineData("80010000", 500, 2200L, 8, 12)] // Curitiba (8): +10% e 2 dias
    public void Preco_e_prazo_pela_tabela(string zip, long grams, long cents, int min, int max) =>
        Assert.Equal([new SupplierFreightOption("manual-economico", cents, "BRL", min, max)], ManualSupplierGateway.Quote(zip, grams, s_options));

    [Fact]
    public void Sem_tabela_configurada_vale_a_padrao() =>
        Assert.Equal(["manual-economico", "manual-expresso"], ManualSupplierGateway.Quote("01310100", 500, new ManualSupplierOptions()).Select(o => o.Service));
}
