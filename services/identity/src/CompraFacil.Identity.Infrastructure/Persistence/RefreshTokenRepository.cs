using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Identity.Application.Accounts;
using CompraFacil.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompraFacil.Identity.Infrastructure.Persistence;

internal sealed class RefreshTokenRepository(IdentityDbContext db) : IRefreshTokenRepository
{
    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);

    public Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct) =>
        db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task<Guid?> FamilyOfAsync(byte[] tokenHash, CancellationToken ct) =>
        await db.RefreshTokens.AsNoTracking().Where(t => t.TokenHash == tokenHash).Select(t => (Guid?)t.FamilyId).SingleOrDefaultAsync(ct);

    // a forma de dois inteiros não colide com a de um bigint (a da rotação de chaves)
    public Task LockFamilyAsync(Guid familyId, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({FamilyLockClass}, hashtext({familyId.ToString()}))", ct);

    private const int FamilyLockClass = 1;

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
        try
        {
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "EmailIndex" or "UserNameIndex" })
        {
            // dois cadastros com o mesmo e-mail ao mesmo tempo: os dois passam na checagem do Identity, o índice barra o segundo
            throw new ConflictException("E-mail já cadastrado", "já existe uma conta com este e-mail");
        }
    }
}
