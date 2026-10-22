using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Orders.Application.Catalog;
using CompraFacil.Orders.Domain;
using FluentValidation;
using FluentValidation.Results;

namespace CompraFacil.Orders.Application.Orders;

public sealed record PlaceOrderItem(Guid ProductId, int Quantity, long? ExpectedUnitPriceCents);

public sealed record PlaceOrderCommand(Guid CustomerId, string CustomerEmail, IReadOnlyList<PlaceOrderItem> Items, string IdempotencyKey);

/// <param name="Order">O pedido (novo ou o já criado com a mesma Idempotency-Key).</param>
/// <param name="Created">false quando a chave já tinha sido usada com o mesmo corpo.</param>
public sealed record PlaceOrderResult(Order Order, bool Created);

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.CustomerEmail).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(c => c.IdempotencyKey).NotEmpty().Length(8, 128);
        RuleFor(c => c.Items).NotEmpty().Must(i => i.Count <= Order.MaxItems)
            .WithMessage($"no máximo {Order.MaxItems} itens");
        RuleFor(c => c.Items).Must(i => i.Select(x => x.ProductId).Distinct().Count() == i.Count)
            .When(c => c.Items is { Count: > 0 })
            .WithMessage("um item por produto: product_id repetido");
        RuleForEach(c => c.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).InclusiveBetween(OrderItem.MinQuantity, OrderItem.MaxQuantity);
            item.RuleFor(i => i.ExpectedUnitPriceCents).GreaterThanOrEqualTo(1).When(i => i.ExpectedUnitPriceCents.HasValue);
        });
    }
}

/// <summary>
/// Cria o pedido e inicia a SAGA: valida os itens e congela os preços no catálogo (gRPC), grava o
/// pedido pendente e o OrderPlaced na mesma transação (outbox). A mesma Idempotency-Key com o mesmo
/// corpo devolve o pedido já criado; com corpo diferente, 422.
/// </summary>
public sealed class PlaceOrder(
    IValidator<PlaceOrderCommand> validator,
    ICatalogPricing catalog,
    IOrderRepository orders,
    IOutbox outbox,
    TimeProvider clock)
{
    public async Task<PlaceOrderResult> HandleAsync(PlaceOrderCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        await validator.ValidateAndThrowAsync(command, ct);
        var requestHash = Hash(command);

        var existing = await orders.FindByIdempotencyKeyAsync(command.CustomerId, command.IdempotencyKey, ct);
        if (existing is not null)
        {
            return Replay(existing, requestHash);
        }

        var priced = await catalog.PriceAsync(
            [.. command.Items.Select(i => new ItemToPrice(i.ProductId, i.Quantity, i.ExpectedUnitPriceCents))], ct);
        EnsureAllSellable(priced);

        var order = Order.Place(
            Guid.CreateVersion7(), command.CustomerId, command.CustomerEmail.Trim(),
            [.. priced.Select(p => new OrderItem(p.ProductId, p.Sku, p.Name, p.Quantity, p.UnitPrice!.Value, p.CampaignId))],
            command.IdempotencyKey, requestHash, clock.GetUtcNow());
        orders.Add(order);
        outbox.Add(OrderEvents.Topic, order.Id.ToString(), OrderEvents.OrderPlaced, OrderPlacedData.From(order));

        try
        {
            await orders.SaveChangesAsync(ct);
            return new PlaceOrderResult(order, Created: true);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            // outra requisição com a mesma chave gravou primeiro: vale a dela
            var winner = await orders.FindByIdempotencyKeyAsync(command.CustomerId, command.IdempotencyKey, ct)
                ?? throw new InvalidOperationException("pedido da Idempotency-Key não encontrado após conflito");
            return Replay(winner, requestHash);
        }
    }

    private static PlaceOrderResult Replay(Order existing, string requestHash)
    {
        if (existing.RequestHash != requestHash)
        {
            throw new ValidationException([new ValidationFailure(nameof(PlaceOrderCommand.IdempotencyKey),
                "Idempotency-Key já usada com outro corpo; use uma chave nova para outro pedido")]);
        }
        return new PlaceOrderResult(existing, Created: false);
    }

    private static void EnsureAllSellable(IReadOnlyList<PricedItem> priced)
    {
        var problems = priced.Where(p => p.Status != PricedItemStatus.Ok).Select(Describe).ToList();
        if (problems.Count > 0)
        {
            throw new ConflictException("Itens recusados pelo catálogo", string.Join("; ", problems));
        }
    }

    private static string Describe(PricedItem p) => p.Status switch
    {
        PricedItemStatus.NotFound => $"{p.ProductId}: produto não existe",
        PricedItemStatus.NotActive => $"{p.ProductId} ({p.Sku}): produto não está à venda",
        PricedItemStatus.PriceChanged => $"{p.ProductId} ({p.Sku}): o preço mudou, agora {p.UnitPrice?.AmountCents} centavos",
        PricedItemStatus.InvalidQuantity => $"{p.ProductId}: quantidade inválida",
        _ => $"{p.ProductId}: {p.Status}",
    };

    /// <summary>Hash do corpo normalizado (ordem dos itens não importa, e-mail sem caixa).</summary>
    internal static string Hash(PlaceOrderCommand c)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            c.CustomerId,
            Email = c.CustomerEmail.Trim().ToUpperInvariant(),
            Items = c.Items.OrderBy(i => i.ProductId).Select(i => new { i.ProductId, i.Quantity, i.ExpectedUnitPriceCents }),
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
