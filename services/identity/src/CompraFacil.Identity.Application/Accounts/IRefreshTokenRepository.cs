using CompraFacil.Identity.Domain;

namespace CompraFacil.Identity.Application.Accounts;

public interface IRefreshTokenRepository
{
    void Add(RefreshToken token);

    Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct);

    /// <summary>Revoga, direto no banco, todos os tokens ainda válidos da família (uma sessão).</summary>
    Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, RevocationReason reason, CancellationToken ct);

    /// <exception cref="BuildingBlocks.Data.ConcurrencyConflictException">Outro refresh trocou o mesmo token antes (xmin).</exception>
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>Uma transação para o que o UserManager grava em mais de um SaveChanges (conta + papel).</summary>
public interface IAccountTransaction
{
    Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct);
}
