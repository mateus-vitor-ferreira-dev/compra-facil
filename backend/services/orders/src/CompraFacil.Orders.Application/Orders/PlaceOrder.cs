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

/// <summary>Como pagar (#220): PIX, ou cartão com o token do provedor, a bandeira e as parcelas.</summary>
public sealed record PlaceOrderPayment(PaymentMethod Method, int Installments, string? CardToken, string? CardBrand);

/// <summary>O pedido a criar; Payment nulo é PIX.</summary>
public sealed record PlaceOrderCommand(
    Guid CustomerId, string CustomerEmail, IReadOnlyList<PlaceOrderItem> Items, string IdempotencyKey, PlaceOrderPayment? Payment = null);

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
        RuleFor(c => c.Payment!).ChildRules(p =>
        {
            p.RuleFor(x => x.Method).IsInEnum();
            p.RuleFor(x => x.CardToken).NotEmpty().MaximumLength(128).When(x => x.Method == PaymentMethod.Card)
                .WithMessage("o cartão precisa do card_token do provedor");
            p.RuleFor(x => x.CardBrand).NotEmpty().MaximumLength(16).When(x => x.Method == PaymentMethod.Card);
            p.RuleFor(x => x.CardToken).Null().When(x => x.Method == PaymentMethod.Pix).WithMessage("PIX não leva card_token");
            p.RuleFor(x => x.Installments).InclusiveBetween(1, 12).When(x => x.Method == PaymentMethod.Card);
            p.RuleFor(x => x.Installments).Equal(1).When(x => x.Method == PaymentMethod.Pix).WithMessage("PIX é à vista");
        }).When(c => c.Payment is not null).OverridePropertyName("payment");
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

        var payment = command.Payment ?? new PlaceOrderPayment(PaymentMethod.Pix, 1, null, null);
        var order = Order.Place(
            Guid.CreateVersion7(), command.CustomerId, command.CustomerEmail.Trim(),
            [.. priced.Select(p => new OrderItem(p.ProductId, p.Sku, p.Name, p.Quantity, p.UnitPrice!.Value, p.CampaignId))],
            command.IdempotencyKey, requestHash, clock.GetUtcNow(), payment.Method, payment.Installments);
        orders.Add(order);
        outbox.Add(OrderEvents.Topic, order.Id.ToString(), OrderEvents.OrderPlaced,
            OrderPlacedData.From(order, payment.CardToken, payment.CardBrand));

        try
        {
            await orders.SaveChangesAsync(ct);
            OrdersMetrics.Placed.Add(1);
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
            // o meio, não o token: o token é de uso único, e o retry do cliente pode trazer outro
            Payment = c.Payment is { } p ? new { p.Method, p.Installments } : null,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
