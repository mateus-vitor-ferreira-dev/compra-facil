using System.Collections.Concurrent;
using System.Net;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Contracts.Catalog.V1;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompraFacil.Orders.IntegrationTests;

/// <summary>Produto do catalog falso.</summary>
public sealed record CatalogProduct(string Sku, string Name, long PriceCents, bool Active = true, Guid? CampaignId = null);

/// <summary>
/// Catalog falso, servindo o mesmo contrato gRPC (contracts/proto/catalog/v1) que o catalog em Go,
/// com as mesmas regras do ValidateItems: NOT_FOUND, NOT_ACTIVE, PRICE_CHANGED e preço efetivo.
/// </summary>
public sealed class FakeCatalog : CatalogService.CatalogServiceBase
{
    public ConcurrentDictionary<Guid, CatalogProduct> Products { get; } = new();

    /// <summary>Simula o catalog fora do ar.</summary>
    public bool Down { get; set; }

    public int Calls => _calls;
    private int _calls;

    public Guid Add(CatalogProduct product)
    {
        var id = Guid.NewGuid();
        Products[id] = product;
        return id;
    }

    public override Task<ValidateItemsResponse> ValidateItems(ValidateItemsRequest request, ServerCallContext context)
    {
        Interlocked.Increment(ref _calls);
        if (Down)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "catalog fora"));
        }
        var response = new ValidateItemsResponse { Valid = true, Total = new Contracts.Catalog.V1.Money { Currency = "BRL" } };
        foreach (var item in request.Items)
        {
            var validated = new ValidatedItem { ProductId = item.ProductId, Quantity = item.Quantity };
            if (!Products.TryGetValue(Guid.Parse(item.ProductId), out var p))
            {
                validated.Status = ItemStatus.NotFound;
            }
            else
            {
                validated.Sku = p.Sku;
                validated.Name = p.Name;
                validated.UnitPrice = new Contracts.Catalog.V1.Money { AmountCents = p.PriceCents, Currency = "BRL" };
                validated.LineTotal = new Contracts.Catalog.V1.Money { AmountCents = p.PriceCents * item.Quantity, Currency = "BRL" };
                validated.CampaignId = p.CampaignId?.ToString() ?? "";
                validated.Status = !p.Active ? ItemStatus.NotActive
                    : item.HasExpectedUnitPriceCents && item.ExpectedUnitPriceCents != p.PriceCents ? ItemStatus.PriceChanged
                    : ItemStatus.Ok;
            }
            if (validated.Status == ItemStatus.Ok)
            {
                response.Total.AmountCents += validated.LineTotal.AmountCents;
            }
            else
            {
                response.Valid = false;
            }
            response.Items.Add(validated);
        }
        return Task.FromResult(response);
    }
}

/// <summary>Postgres real + catalog falso (gRPC de verdade, HTTP/2) + o orders em memória.</summary>
/// <summary>O fulfillment dos testes: cota dois fretes (ou nenhum, ou fora do ar), e conta as cotações.</summary>
public sealed class FakeShippingQuotes : CompraFacil.Orders.Application.Shipping.IShippingQuotes
{
    public List<CompraFacil.Orders.Application.Shipping.ShippingQuote>? Options { get; set; } =
        [new("economico", "Econômico", 2430, 13, 21), new("expresso", "Expresso", 6480, 7, 11)];

    public List<(string Zip, IReadOnlyList<(string Sku, int Quantity)> Items)> Calls { get; } = [];

    public Task<IReadOnlyList<CompraFacil.Orders.Application.Shipping.ShippingQuote>> QuoteAsync(
        string zipCode, IReadOnlyList<(string Sku, int Quantity)> items, CancellationToken ct)
    {
        Calls.Add((zipCode, items));
        return Options is { } options
            ? Task.FromResult<IReadOnlyList<CompraFacil.Orders.Application.Shipping.ShippingQuote>>([.. options])
            : throw new CompraFacil.BuildingBlocks.Web.DependencyUnavailableException("frete fora do ar", TimeSpan.FromSeconds(5));
    }
}

public class OrdersFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private WebApplication _catalogHost = null!;

    public FakeCatalog Catalog { get; } = new();
    public FakeShippingQuotes Shipping { get; } = new();

    /// <summary>Releitura do pedido nos streams SSE; longa, só o aviso (OrderChangeFeed) traz a mudança.</summary>
    protected virtual string LivePollInterval => "00:00:00.200";

    /// <summary>O identity dos testes: o pedido valida os tokens dele pelo JWKS (Auth:Jwks).</summary>
    public TestIdentity Identity { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, o => o.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(Catalog);
        _catalogHost = builder.Build();
        _catalogHost.MapGrpcService<FakeCatalog>();
        await _catalogHost.StartAsync();
        var catalogAddress = _catalogHost.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _postgres.ConnectionString)
            .UseSetting("Catalog:GrpcAddress", catalogAddress)
            .UseSetting("Catalog:BreakDuration", "00:00:00.500") // o circuito fecha rápido entre testes
            .UseSetting("Kafka:Enabled", "false") // aqui o evento é conferido no outbox
            .UseSetting("Auth:Jwks", Identity.Jwks)
            // acompanhamento ao vivo em ritmo de teste
            .UseSetting("Orders:Live:PollInterval", LivePollInterval)
            .UseSetting("Orders:Live:HeartbeatInterval", "00:00:00.300")
            .UseSetting("Orders:Live:MaxDuration", "00:00:03")
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<CompraFacil.Orders.Application.Shipping.IShippingQuotes>();
                services.AddSingleton<CompraFacil.Orders.Application.Shipping.IShippingQuotes>(Shipping);
            }));
        using var _ = Factory.CreateClient(); // sobe e migra
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _catalogHost.DisposeAsync();
        await _postgres.DisposeAsync();
        Identity.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Cliente HTTP com o token de um cliente da loja (um novo, se não vier o id).</summary>
    public HttpClient ClientAs(Guid? customerId = null, string email = "ana@exemplo.com")
    {
        var http = Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", Identity.CustomerToken(customerId, email));
        return http;
    }
}
