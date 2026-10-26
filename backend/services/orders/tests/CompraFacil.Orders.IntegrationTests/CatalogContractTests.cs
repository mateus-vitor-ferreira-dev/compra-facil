using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Contracts.Catalog.V1;
using CompraFacil.Orders.Application.Catalog;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Orders.IntegrationTests;

// ---------------------------------------------------------------------------------------------
// Contrato consumer-driven orders → catalog (ADR 0004). O orders declara aqui as interações do
// ValidateItems de que depende; o arquivo contracts/consumer/orders-catalog.json sai delas, e o
// catalog o verifica contra o servidor gRPC real (services/catalog/test/contract_orders_test.go).
// Só entram os campos que o orders lê: o catalog pode mandar mais sem quebrar o contrato.
// ---------------------------------------------------------------------------------------------

/// <summary>Produto que o provedor precisa ter antes da interação (estado do provedor).</summary>
public sealed record GivenProduct(string Status, long PriceCents, int? CampaignDiscountPercent = null);

public sealed record ContractRequestItem(string Product, int Quantity, long? ExpectedUnitPriceCents = null);

public sealed record ContractMoney(long AmountCents, string Currency);

/// <summary>Item esperado na resposta; "&lt;string&gt;" e "&lt;uuid&gt;" casam com qualquer valor do tipo.</summary>
public sealed record ContractResponseItem(
    string Product, string Status, int? Quantity = null, string? Sku = null, string? Name = null,
    ContractMoney? UnitPrice = null, string? CampaignId = null);

public sealed record ContractInteraction(
    string Description,
    IReadOnlyDictionary<string, GivenProduct> Given,
    IReadOnlyList<ContractRequestItem> Request,
    IReadOnlyList<ContractResponseItem> Response);

public sealed record ConsumerContract(
    string Consumer, string Provider, string Rpc, IReadOnlyList<string> Rules, IReadOnlyList<ContractInteraction> Interactions);

public static class OrdersCatalogContract
{
    public const string AnyString = "<string>";
    public const string AnyUuid = "<uuid>";
    public const string FileName = "consumer/orders-catalog.json";

    private static readonly GivenProduct s_active = new("active", 1990);

    public static readonly ConsumerContract Contract = new(
        Consumer: "orders",
        Provider: "catalog",
        Rpc: "catalog.v1.CatalogService/ValidateItems",
        Rules:
        [
            "given: produtos que o catalog cria antes da interação; a chave é a referência usada em request/response",
            "produto citado no request e ausente de given não existe no catalog",
            "response: um item por produto do request; só os campos listados são verificados",
            $"\"{AnyString}\" casa com qualquer texto não vazio; \"{AnyUuid}\" com qualquer UUID; \"\" exige vazio",
        ],
        Interactions:
        [
            new("produto ativo sem campanha: OK, com o preço de tabela",
                new Dictionary<string, GivenProduct> { ["p1"] = s_active },
                [new("p1", 2)],
                [new("p1", "ITEM_STATUS_OK", 2, AnyString, AnyString, new(1990, "BRL"), "")]),
            new("produto em campanha de 25%: OK, com o preço com desconto (arredondado para baixo) e a campanha",
                new Dictionary<string, GivenProduct> { ["p1"] = s_active with { CampaignDiscountPercent = 25 } },
                [new("p1", 1)],
                [new("p1", "ITEM_STATUS_OK", 1, AnyString, AnyString, new(1492, "BRL"), AnyUuid)]),
            new("preço esperado diferente do atual: PRICE_CHANGED, com o preço atual",
                new Dictionary<string, GivenProduct> { ["p1"] = s_active },
                [new("p1", 1, ExpectedUnitPriceCents: 1000)],
                [new("p1", "ITEM_STATUS_PRICE_CHANGED", Sku: AnyString, UnitPrice: new(1990, "BRL"))]),
            new("produto fora de venda: NOT_ACTIVE",
                new Dictionary<string, GivenProduct> { ["p1"] = s_active with { Status = "inactive" } },
                [new("p1", 1)],
                [new("p1", "ITEM_STATUS_NOT_ACTIVE", Sku: AnyString)]),
            new("produto que não existe: NOT_FOUND",
                new Dictionary<string, GivenProduct>(),
                [new("fantasma", 1)],
                [new("fantasma", "ITEM_STATUS_NOT_FOUND")]),
            new("pedido com vários itens: cada um com o próprio resultado",
                new Dictionary<string, GivenProduct> { ["p1"] = s_active, ["p2"] = new("active", 500) },
                [new("p1", 1), new("fantasma", 1), new("p2", 3)],
                [
                    new("p1", "ITEM_STATUS_OK", 1, AnyString, AnyString, new(1990, "BRL"), ""),
                    new("fantasma", "ITEM_STATUS_NOT_FOUND"),
                    new("p2", "ITEM_STATUS_OK", 3, AnyString, AnyString, new(500, "BRL"), ""),
                ]),
        ]);

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // acentos legíveis no arquivo
    };
}

/// <summary>
/// Catalog falso que responde exatamente o que o contrato promete, e confere se o orders pediu o
/// que o contrato diz (como o mock provider do Pact).
/// </summary>
public sealed class ContractCatalog : CatalogService.CatalogServiceBase
{
    public ContractInteraction? Current { get; set; }
    public IReadOnlyDictionary<string, Guid> Ids { get; set; } = new Dictionary<string, Guid>();
    public List<string> Mismatches { get; } = [];

    public override Task<ValidateItemsResponse> ValidateItems(ValidateItemsRequest request, ServerCallContext context)
    {
        var interaction = Current ?? throw new RpcException(new Status(StatusCode.FailedPrecondition, "sem interação"));
        var expected = interaction.Request.Select(i => (Ids[i.Product].ToString(), i.Quantity, i.ExpectedUnitPriceCents)).ToList();
        var actual = request.Items.Select(i => (i.ProductId, i.Quantity, i.HasExpectedUnitPriceCents ? i.ExpectedUnitPriceCents : (long?)null)).ToList();
        if (!expected.SequenceEqual(actual))
        {
            Mismatches.Add($"{interaction.Description}: request {string.Join(", ", actual)} ≠ contrato {string.Join(", ", expected)}");
        }

        var response = new ValidateItemsResponse();
        foreach (var item in interaction.Response)
        {
            var validated = new ValidatedItem
            {
                ProductId = Ids[item.Product].ToString(),
                Status = Enum.Parse<ItemStatus>(item.Status.Replace("ITEM_STATUS_", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal), ignoreCase: true),
                Quantity = item.Quantity ?? 0,
                Sku = Example(item.Sku),
                Name = Example(item.Name),
                CampaignId = Example(item.CampaignId),
            };
            if (item.UnitPrice is { } price)
            {
                validated.UnitPrice = new Money { AmountCents = price.AmountCents, Currency = price.Currency };
            }
            response.Items.Add(validated);
        }
        return Task.FromResult(response);
    }

    // valor de exemplo para cada matcher (o que um catalog real poderia mandar)
    private static string Example(string? value) => value switch
    {
        OrdersCatalogContract.AnyString => "EXEMPLO-CONTRATO",
        OrdersCatalogContract.AnyUuid => Guid.NewGuid().ToString(),
        null => "",
        _ => value,
    };
}

/// <summary>O orders (DI real: pipeline do Polly + gRPC) contra o catalog do contrato.</summary>
public sealed class CatalogContractFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private WebApplication _catalogHost = null!;

    public ContractCatalog Catalog { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, o => o.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(Catalog);
        _catalogHost = builder.Build();
        _catalogHost.MapGrpcService<ContractCatalog>();
        await _catalogHost.StartAsync();
        var address = _catalogHost.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _postgres.ConnectionString)
            .UseSetting("Catalog:GrpcAddress", address)
            .UseSetting("Kafka:Enabled", "false"));
        using var _ = Factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _catalogHost.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

public sealed class CatalogContractTests(CatalogContractFixture fx) : IClassFixture<CatalogContractFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Interactions() =>
        [.. OrdersCatalogContract.Contract.Interactions.Select(i => i.Description)];

    [Theory]
    [MemberData(nameof(Interactions))]
    public async Task Orders_UnderstandsEveryResponseInTheContract(string description)
    {
        var interaction = OrdersCatalogContract.Contract.Interactions.Single(i => i.Description == description);
        var ids = interaction.Request.Select(r => r.Product).Concat(interaction.Response.Select(r => r.Product))
            .Distinct().ToDictionary(r => r, _ => Guid.NewGuid());
        fx.Catalog.Ids = ids;
        fx.Catalog.Current = interaction;

        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var pricing = scope.ServiceProvider.GetRequiredService<ICatalogPricing>();
        var priced = await pricing.PriceAsync(
            [.. interaction.Request.Select(r => new ItemToPrice(ids[r.Product], r.Quantity, r.ExpectedUnitPriceCents))], Ct);

        Assert.Empty(fx.Catalog.Mismatches); // o orders pediu o que o contrato diz
        Assert.Equal(interaction.Response.Count, priced.Count);
        foreach (var expected in interaction.Response)
        {
            var item = Assert.Single(priced, p => p.ProductId == ids[expected.Product]);
            Assert.Equal(StatusOf(expected.Status), item.Status);
            if (expected.UnitPrice is { } price)
            {
                Assert.Equal(price.AmountCents, item.UnitPrice!.Value.AmountCents);
                Assert.Equal(price.Currency, item.UnitPrice.Value.Currency);
            }
            if (expected.Quantity is { } quantity)
            {
                Assert.Equal(quantity, item.Quantity);
            }
            if (expected.Sku is not null)
            {
                Assert.False(string.IsNullOrEmpty(item.Sku));
            }
            if (expected.CampaignId is not null)
            {
                Assert.Equal(expected.CampaignId == OrdersCatalogContract.AnyUuid, item.CampaignId is not null);
            }
        }
    }

    /// <summary>
    /// O arquivo do contrato é gerado das interações acima. Mudou o que o orders espera: rode com
    /// UPDATE_CONTRACTS=1 para regravar, e o pipeline do catalog verifica a nova versão.
    /// </summary>
    [Fact]
    public void ContractFile_IsUpToDate()
    {
        var path = Path.Combine(ContractsDir(), OrdersCatalogContract.FileName);
        var generated = JsonSerializer.Serialize(OrdersCatalogContract.Contract, OrdersCatalogContract.Json) + "\n";
        if (Environment.GetEnvironmentVariable("UPDATE_CONTRACTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, generated);
        }
        Assert.True(File.Exists(path), $"{path} não existe: rode os testes com UPDATE_CONTRACTS=1");
        Assert.Equal(generated, File.ReadAllText(path));
    }

    private static PricedItemStatus StatusOf(string status) => status switch
    {
        "ITEM_STATUS_OK" => PricedItemStatus.Ok,
        "ITEM_STATUS_NOT_FOUND" => PricedItemStatus.NotFound,
        "ITEM_STATUS_NOT_ACTIVE" => PricedItemStatus.NotActive,
        "ITEM_STATUS_PRICE_CHANGED" => PricedItemStatus.PriceChanged,
        "ITEM_STATUS_INVALID_QUANTITY" => PricedItemStatus.InvalidQuantity,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "status fora do contrato"),
    };

    private static string ContractsDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "contracts");
            if (Directory.Exists(Path.Combine(candidate, "proto")))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException("pasta contracts/ não encontrada acima de " + AppContext.BaseDirectory);
    }
}
