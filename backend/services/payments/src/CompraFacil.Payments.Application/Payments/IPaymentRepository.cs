using CompraFacil.Payments.Domain;

namespace CompraFacil.Payments.Application.Payments;

public interface IPaymentRepository
{
    /// <summary>Cobrança pendente do pedido, rastreada (para alterar).</summary>
    Task<Charge?> GetChargeAsync(Guid orderId, CancellationToken ct);

    void Add(Charge charge);

    void Add(Payment payment);

    Task<Payment?> GetPaymentAsync(Guid id, CancellationToken ct);

    /// <summary>O pagamento rastreado (para estornar).</summary>
    Task<Payment?> GetPaymentForUpdateAsync(Guid id, CancellationToken ct);

    /// <summary>A cobrança de um pedido pela referência no provedor, rastreada (o webhook).</summary>
    Task<Payment?> GetByReferenceForUpdateAsync(string provider, string reference, CancellationToken ct);

    /// <summary>O pagamento pago (autorizado ou aprovado) do pedido, rastreado (a captura).</summary>
    Task<Payment?> GetPaidForUpdateAsync(Guid orderId, CancellationToken ct);

    Task<List<Payment>> ListByOrderAsync(Guid orderId, CancellationToken ct);

    /// <exception cref="BuildingBlocks.Data.ConcurrencyConflictException">Outra transação mudou ou criou a cobrança.</exception>
    Task SaveChangesAsync(CancellationToken ct);

    void Reset();
}
