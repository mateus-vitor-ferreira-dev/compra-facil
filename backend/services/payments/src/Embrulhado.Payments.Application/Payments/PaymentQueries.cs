using Embrulhado.BuildingBlocks.Web;
using Embrulhado.Payments.Domain;

namespace Embrulhado.Payments.Application.Payments;

public sealed class PaymentQueries(IPaymentRepository payments)
{
    public async Task<Payment> GetAsync(Guid id, CancellationToken ct) =>
        await payments.GetPaymentAsync(id, ct) ?? throw new NotFoundException($"pagamento {id} não encontrado");

    public Task<List<Payment>> ListByOrderAsync(Guid orderId, CancellationToken ct) => payments.ListByOrderAsync(orderId, ct);
}
