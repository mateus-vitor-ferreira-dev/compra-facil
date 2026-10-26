using System.Security.Cryptography;
using System.Text;
using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Inventory.Domain;
using FluentValidation;
using FluentValidation.Results;

namespace CompraFacil.Inventory.Application.Stock;

/// <summary>Saldo exposto pela API (StockLevel do contrato).</summary>
public sealed record StockLevel(Guid ProductId, int OnHand, int Reserved, int Available, DateTimeOffset UpdatedAt)
{
    public static StockLevel From(StockItem s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(s.ProductId, s.OnHand, s.Reserved, s.Available, s.UpdatedAt);
    }
}

public sealed record AdjustStockCommand(Guid ProductId, int Delta, string Reason, string? Note, string IdempotencyKey, string Operator);

public sealed class AdjustStockValidator : AbstractValidator<AdjustStockCommand>
{
    public static readonly string[] Reasons = ["restock", "inventory_count", "damage", "other"];

    public AdjustStockValidator()
    {
        RuleFor(c => c.Delta).NotEqual(0).InclusiveBetween(-100_000, 100_000);
        RuleFor(c => c.Reason).Must(r => Reasons.Contains(r)).WithMessage($"deve ser um de: {string.Join(", ", Reasons)}");
        RuleFor(c => c.Note).MaximumLength(500);
        RuleFor(c => c.IdempotencyKey).NotEmpty().Length(8, 128);
    }
}

/// <summary>
/// Ajuste do estoque físico pelo time (entrada, inventário, avaria). Recusa (409) se o saldo ficar
/// abaixo do reservado; cria o registro se o produto ainda não tem. Mesma Idempotency-Key com o mesmo
/// corpo devolve o saldo da primeira vez; com outro corpo, 422.
/// </summary>
public sealed class AdjustStock(IStockRepository stock, IValidator<AdjustStockCommand> validator, TimeProvider clock)
{
    public async Task<StockLevel> HandleAsync(AdjustStockCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        await validator.ValidateAndThrowAsync(command, ct);
        var hash = Hash(command);

        var previous = await stock.FindAdjustmentAsync(command.ProductId, command.IdempotencyKey, ct);
        if (previous is not null)
        {
            return Replay(previous, hash);
        }

        try
        {
            return await OptimisticConcurrency.RetryAsync(stock.Reset, async () =>
            {
                var now = clock.GetUtcNow();
                var item = (await stock.GetForUpdateAsync([command.ProductId], ct)).SingleOrDefault();
                if (item is null)
                {
                    item = StockItem.Create(command.ProductId, 0, now);
                    stock.Add(item);
                }
                try
                {
                    item.Adjust(command.Delta, now);
                }
                catch (DomainException e)
                {
                    throw new ConflictException("Ajuste recusado", e.Message);
                }
                stock.Add(new StockAdjustmentRecord(command.ProductId, command.IdempotencyKey, hash, command.Delta, command.Reason,
                    command.Note, command.Operator, item.OnHand, item.Reserved, now));
                await stock.SaveChangesAsync(ct);
                return StockLevel.From(item);
            });
        }
        catch (DuplicateAdjustmentException)
        {
            stock.Reset();
            var winner = await stock.FindAdjustmentAsync(command.ProductId, command.IdempotencyKey, ct)
                ?? throw new InvalidOperationException("ajuste da Idempotency-Key não encontrado após conflito");
            return Replay(winner, hash);
        }
    }

    private static StockLevel Replay(StockAdjustmentRecord previous, string hash)
    {
        if (previous.RequestHash != hash)
        {
            throw new ValidationException([new ValidationFailure(nameof(AdjustStockCommand.IdempotencyKey),
                "Idempotency-Key já usada com outro corpo; use uma chave nova para outro ajuste")]);
        }
        return new StockLevel(previous.ProductId, previous.OnHandAfter, previous.ReservedAfter,
            previous.OnHandAfter - previous.ReservedAfter, previous.CreatedAt);
    }

    private static string Hash(AdjustStockCommand c) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes($"{c.ProductId}|{c.Delta}|{c.Reason}|{c.Note}")));
}

/// <summary>Consultas de saldo e de reserva.</summary>
public sealed class StockQueries(IStockRepository stock)
{
    public const int MaxProducts = 100;

    public async Task<StockLevel> GetAsync(Guid productId, CancellationToken ct) =>
        await stock.FindAsync(productId, ct) is { } item
            ? StockLevel.From(item)
            : throw new NotFoundException($"produto {productId} sem registro de estoque");

    /// <summary>Produtos sem registro aparecem zerados (contrato listStock).</summary>
    public async Task<IReadOnlyList<StockLevel>> ListAsync(IReadOnlyList<Guid> productIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        if (productIds.Count is 0 or > MaxProducts)
        {
            throw new ValidationException([new ValidationFailure("ProductIds", $"informe de 1 a {MaxProducts} produtos")]);
        }
        var found = (await stock.ListAsync([.. productIds.Distinct()], ct)).ToDictionary(s => s.ProductId);
        return [.. productIds.Distinct().Select(id => found.TryGetValue(id, out var s)
            ? StockLevel.From(s)
            : new StockLevel(id, 0, 0, 0, DateTimeOffset.UnixEpoch))];
    }

    public async Task<Reservation> GetReservationAsync(Guid orderId, CancellationToken ct) =>
        await stock.GetReservationAsync(orderId, forUpdate: false, ct)
            ?? throw new NotFoundException($"pedido {orderId} sem reserva");
}
