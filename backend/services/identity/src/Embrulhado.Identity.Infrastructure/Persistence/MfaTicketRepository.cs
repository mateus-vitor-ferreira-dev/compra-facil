using Embrulhado.Identity.Application.Accounts;
using Embrulhado.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Embrulhado.Identity.Infrastructure.Persistence;

internal sealed class MfaTicketRepository(IdentityDbContext db) : IMfaTicketRepository
{
    public void Add(MfaTicket ticket) => db.MfaTickets.Add(ticket);

    public Task<MfaTicket?> FindByHashAsync(byte[] tokenHash, CancellationToken ct) =>
        db.MfaTickets.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<bool> TryConsumeAsync(Guid ticketId, DateTimeOffset now, CancellationToken ct) =>
        await db.MfaTickets
            .Where(t => t.Id == ticketId && t.ConsumedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, now), ct) == 1;

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
