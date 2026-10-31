using CompraFacil.Identity.Application.Accounts;
using CompraFacil.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Identity.Infrastructure.Persistence;

internal sealed class GuestRepository(IdentityDbContext db) : IGuestRepository
{
    public Task<User?> FindByCheckoutKeyAsync(Guid checkoutKey, CancellationToken ct) =>
        db.Users.SingleOrDefaultAsync(u => u.GuestCheckoutKey == checkoutKey, ct);

    public async Task<IReadOnlyList<User>> RecentByEmailAsync(string normalizedEmail, int take, CancellationToken ct) =>
        await db.Users
            .Where(u => u.NormalizedGuestEmail == normalizedEmail)
            .OrderByDescending(u => u.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public void AddLink(GuestAccessLink link) => db.GuestAccessLinks.Add(link);

    public Task<GuestAccessLink?> FindLinkAsync(byte[] tokenHash, CancellationToken ct) =>
        db.GuestAccessLinks.AsNoTracking().SingleOrDefaultAsync(l => l.TokenHash == tokenHash, ct);

    public async Task<DateTimeOffset?> LastLinkAtAsync(Guid userId, CancellationToken ct) =>
        await db.GuestAccessLinks.AsNoTracking()
            .Where(l => l.UserId == userId)
            .MaxAsync(l => (DateTimeOffset?)l.CreatedAt, ct);
}
