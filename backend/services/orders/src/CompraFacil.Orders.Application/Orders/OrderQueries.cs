using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Orders.Domain;
using FluentValidation;
using FluentValidation.Results;

namespace CompraFacil.Orders.Application.Orders;

public sealed record ListOrdersQuery(Guid CustomerId, OrderStatus? Status, string? Cursor, int Limit = ListOrdersQuery.DefaultLimit)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;
}

public sealed class ListOrdersValidator : AbstractValidator<ListOrdersQuery>
{
    public ListOrdersValidator()
    {
        RuleFor(q => q.CustomerId).NotEmpty();
        RuleFor(q => q.Limit).InclusiveBetween(1, ListOrdersQuery.MaxLimit);
    }
}

/// <summary>Quem lê: o cliente do token (<c>sub</c>) e se é operador (papel <c>admin</c>).</summary>
public sealed record OrderReader(Guid? CustomerId, bool IsOperator);

/// <summary>Leitura de pedidos (detalhe e listagem por cliente).</summary>
public sealed class OrderQueries(IOrderRepository orders, IValidator<ListOrdersQuery> listValidator)
{
    /// <summary>
    /// Pedido visto por <paramref name="reader"/>: o dono ou um operador. De outro cliente, 404 como se
    /// não existisse (o id não revela que o pedido existe).
    /// </summary>
    public async Task<Order> GetAsync(Guid id, OrderReader reader, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var order = await orders.GetAsync(id, ct);
        return order is not null && (reader.IsOperator || order.CustomerId == reader.CustomerId)
            ? order
            : throw new NotFoundException($"pedido {id} não encontrado");
    }

    public async Task<OrderPage> ListAsync(ListOrdersQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        await listValidator.ValidateAndThrowAsync(query, ct);
        try
        {
            return await orders.ListByCustomerAsync(query.CustomerId, query.Status, query.Cursor, query.Limit, ct);
        }
        catch (InvalidCursorException)
        {
            throw new ValidationException([new ValidationFailure(nameof(query.Cursor), "cursor inválido ou de outra listagem")]);
        }
    }
}
