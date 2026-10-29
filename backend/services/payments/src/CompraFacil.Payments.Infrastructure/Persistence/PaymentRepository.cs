using CompraFacil.BuildingBlocks.Data;
using CompraFacil.Payments.Application.Payments;
using CompraFacil.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompraFacil.Payments.Infrastructure.Persistence;

internal sealed class PaymentRepository(PaymentsDbContext db) : IPaymentRepository
{
    public Task<Charge?> GetChargeAsync(Guid orderId, CancellationToken ct) =>
        db.Charges.SingleOrDefaultAsync(c => c.OrderId == orderId, ct);

    public void Add(Charge charge) => db.Charges.Add(charge);

    public void Add(Payment payment) => db.Payments.Add(payment);

    public Task<Payment?> GetPaymentAsync(Guid id, CancellationToken ct) =>
        db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);

    public Task<Payment?> GetPaymentForUpdateAsync(Guid id, CancellationToken ct) =>
        db.Payments.SingleOrDefaultAsync(p => p.Id == id, ct);

    public Task<List<Payment>> ListByOrderAsync(Guid orderId, CancellationToken ct) =>
        db.Payments.AsNoTracking().Where(p => p.OrderId == orderId).OrderBy(p => p.CreatedAt).ToListAsync(ct);

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new ConcurrencyConflictException("cobrança alterada por outra transação", e);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // dois eventos do mesmo pedido criaram a cobrança ao mesmo tempo: a nova tentativa a encontra
            throw new ConcurrencyConflictException("cobrança criada por outra transação", e);
        }
    }

    public void Reset() => db.ChangeTracker.Clear();
}
