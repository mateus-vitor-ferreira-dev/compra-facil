using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Fulfillment.Api.Controllers;

/// <summary>
/// checkSupplierStock (contracts/openapi/fulfillment.v1.yaml): o inventory confere no fornecedor, antes de
/// reservar e cobrar, as variantes de um pedido (#218). Rota interna: o gateway não a expõe, só a rede dos
/// serviços chega nela.
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class InternalStockController(SupplierStock stock) : ControllerBase
{
    public const int MaxItems = 50;

    [HttpPost("v1/internal/supplier-stock/check")]
    public async Task<StockCheckResponse> Check(StockCheckRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Items is not { Count: > 0 and <= MaxItems } items || items.Any(i => string.IsNullOrWhiteSpace(i.Vid) || i.Quantity < 1))
        {
            throw new ValidationException([new ValidationFailure("Items", $"de 1 a {MaxItems} itens, cada um com vid e quantity >= 1")]);
        }
        try
        {
            var results = await stock.CheckAsync([.. items.Select(i => new StockCheckItem(i.Vid!, i.Quantity))], ct);
            return new StockCheckResponse([.. results.Select(r => new StockCheckLine(r.Vid, r.Available, r.Known))]);
        }
        catch (SupplierUnavailableException e)
        {
            throw new DependencyUnavailableException($"o fornecedor não respondeu: {e.Message}", TimeSpan.FromSeconds(5));
        }
    }
}

public sealed record StockCheckRequest(IReadOnlyList<StockCheckRequestItem>? Items);

public sealed record StockCheckRequestItem(string? Vid, int Quantity);

public sealed record StockCheckResponse(IReadOnlyList<StockCheckLine> Items);

/// <summary>Known é falso para vid que o fornecedor não tem no feed (a disponibilidade vem como ele respondeu).</summary>
public sealed record StockCheckLine(string Vid, int Available, bool Known);
