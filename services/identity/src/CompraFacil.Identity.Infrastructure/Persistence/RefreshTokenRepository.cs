using CompraFacil.BuildingBlocks.Data;
using CompraFacil.Identity.Application.Accounts;
using CompraFacil.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Identity.Infrastructure.Persistence;

internal sealed class RefreshTokenRepository(IdentityDbContext db) : IRefreshTokenRepository
{
    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);

    public Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct) =>
        db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, RevocationReason reason, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevocationReason, reason), ct);

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new ConcurrencyConflictException("o refresh token mudou desde a leitura", e);
        }
    }
}

internal sealed class AccountTransaction(IdentityDbContext db) : IAccountTransaction
{
    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await work();
        await tx.CommitAsync(ct);
        return result;
    }
}
