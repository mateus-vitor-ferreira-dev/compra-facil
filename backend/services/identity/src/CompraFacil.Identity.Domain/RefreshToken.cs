namespace CompraFacil.Identity.Domain;

public enum RevocationReason
{
    /// <summary>Trocado por um novo no refresh (rotação).</summary>
    Rotated,

    /// <summary>Logout do cliente.</summary>
    Logout,

    /// <summary>Um token já trocado foi apresentado de novo: a família inteira cai (provável roubo).</summary>
    Reuse,
}

/// <summary>
/// Refresh token rotativo. Cada uso troca o token por um novo da mesma família (um login = uma
/// família); apresentar um token já trocado revoga a família toda, porque só quem o copiou o teria.
/// A exceção é o intervalo de reuso: logo depois da troca, o mesmo token ainda dá um irmão na família
/// (requisições paralelas do mesmo navegador). Só o hash (SHA-256) é guardado: o banco vazado não dá
/// sessões.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public byte[] TokenHash { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public RevocationReason? RevocationReason { get; private set; }
    public Guid? ReplacedById { get; private set; }

    /// <summary>Concorrência otimista (xmin): dois refresh simultâneos com o mesmo token não trocam os dois.</summary>
    public uint Version { get; private set; }

    private RefreshToken()
    {
    }

    /// <summary>Primeiro token de uma família (login).</summary>
    public static RefreshToken Issue(Guid userId, byte[] tokenHash, DateTimeOffset now, TimeSpan lifetime) =>
        New(userId, Guid.CreateVersion7(), tokenHash, now, lifetime);

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    /// <summary>Já foi trocado por outro: apresentá-lo de novo é reuso.</summary>
    public bool WasRotated => RevocationReason == Domain.RevocationReason.Rotated;

    /// <summary>
    /// Foi trocado há no máximo <paramref name="interval"/> e ainda não venceu: apresentado de novo, é
    /// uma requisição paralela da troca, não uma cópia.
    /// </summary>
    public bool RotatedWithin(DateTimeOffset now, TimeSpan interval) =>
        WasRotated && interval > TimeSpan.Zero && now - RevokedAt!.Value <= interval && now < ExpiresAt;

    /// <summary>
    /// Um token novo da mesma família, para a requisição que chegou com este token logo depois da troca
    /// (dentro do intervalo de reuso). Este continua trocado; o irmão vale <paramref name="lifetime"/>.
    /// </summary>
    public RefreshToken Sibling(byte[] tokenHash, DateTimeOffset now, TimeSpan lifetime, TimeSpan reuseInterval)
    {
        if (!RotatedWithin(now, reuseInterval))
        {
            throw new InvalidOperationException("só um token trocado há pouco tem irmão; fora do intervalo é reuso");
        }
        return New(UserId, FamilyId, tokenHash, now, lifetime);
    }

    /// <summary>Troca este token pelo próximo da família, que vale <paramref name="lifetime"/> a partir de agora.</summary>
    public RefreshToken Rotate(byte[] nextTokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        if (!IsActive(now))
        {
            throw new InvalidOperationException("refresh token inativo não pode ser trocado");
        }
        var next = New(UserId, FamilyId, nextTokenHash, now, lifetime);
        Revoke(now, Domain.RevocationReason.Rotated);
        ReplacedById = next.Id;
        return next;
    }

    /// <summary>Revoga (idempotente: a primeira revogação e o motivo dela ficam).</summary>
    public void Revoke(DateTimeOffset now, RevocationReason reason)
    {
        if (RevokedAt is not null)
        {
            return;
        }
        RevokedAt = now;
        RevocationReason = reason;
    }

    private static RefreshToken New(Guid userId, Guid familyId, byte[] tokenHash, DateTimeOffset now, TimeSpan lifetime) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        FamilyId = familyId,
        TokenHash = tokenHash,
        CreatedAt = now,
        ExpiresAt = now + lifetime,
    };
}
