using CompraFacil.Fulfillment.Application.Invoices;
using CompraFacil.Fulfillment.Domain;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Fulfillment.Infrastructure.Persistence;

internal sealed class InvoiceRepository(FulfillmentDbContext db) : IInvoiceRepository
{
    public void Add(Invoice invoice) => db.Invoices.Add(invoice);

    public Task<Invoice?> GetAsync(Guid orderId, CancellationToken ct) => db.Invoices.SingleOrDefaultAsync(i => i.Id == orderId, ct);

    public Task<Dictionary<Guid, string>> ReturnErrorsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken ct) =>
        db.Invoices.AsNoTracking().Where(i => orderIds.Contains(i.Id) && i.ReturnError != null)
            .ToDictionaryAsync(i => i.Id, i => i.ReturnError!, ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
