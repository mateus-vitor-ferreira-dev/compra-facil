using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Fulfillment.Application.Shipping;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Fulfillment.Api.Controllers;

/// <summary>
/// quoteShipping (contracts/openapi/fulfillment.v1.yaml): o frete e o prazo por CEP (#222), na página do
/// produto, no carrinho e no checkout. Público: o cliente cota antes de entrar na conta. O orders usa a
/// mesma rota para conferir o frete do pedido (o preço não vem do navegador).
/// </summary>
[ApiController]
[AllowAnonymous]
[Produces("application/json")]
public sealed class ShippingController(ShippingQuotes quotes) : ControllerBase
{
    [HttpPost("v1/shipping/quote")]
    public async Task<ShippingQuoteResponse> Quote(ShippingQuoteRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var failures = new List<ValidationFailure>();
        var zip = new string([.. (body.ZipCode ?? "").Where(char.IsAsciiDigit)]);
        if (zip.Length != 8)
        {
            failures.Add(new ValidationFailure("zip_code", "CEP com 8 dígitos"));
        }
        if (body.Items is not { Count: > 0 and <= ShippingQuotes.MaxItems } items
            || items.Any(i => string.IsNullOrWhiteSpace(i.Sku) || i.Quantity is not (>= 1 and <= 99)))
        {
            failures.Add(new ValidationFailure("items", $"de 1 a {ShippingQuotes.MaxItems} itens, cada um com sku e quantity de 1 a 99"));
        }
        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
        try
        {
            var options = await quotes.QuoteAsync(zip, [.. body.Items!.Select(i => new ShippingItem(i.Sku!, i.Quantity))], ct);
            return new ShippingQuoteResponse(zip, [.. options]);
        }
        catch (SupplierUnavailableException e)
        {
            throw new DependencyUnavailableException($"o fornecedor não cotou o frete: {e.Message}", TimeSpan.FromSeconds(5));
        }
    }
}

public sealed record ShippingQuoteRequest(string? ZipCode, IReadOnlyList<ShippingQuoteItem>? Items);

public sealed record ShippingQuoteItem(string? Sku, int Quantity);

public sealed record ShippingQuoteResponse(string ZipCode, IReadOnlyList<ShippingOption> Options);
