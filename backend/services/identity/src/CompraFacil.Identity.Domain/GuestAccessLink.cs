namespace CompraFacil.Identity.Domain;

/// <summary>
/// O link enviado por e-mail para quem comprou sem conta voltar aos pedidos. O banco guarda só o hash, e o
/// link pode ser aberto mais de uma vez até vencer: o prazo de desistência depende dele.
/// </summary>
public sealed class GuestAccessLink
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public byte[] TokenHash { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => now < ExpiresAt;

    public static GuestAccessLink Issue(Guid userId, byte[] tokenHash, DateTimeOffset now, TimeSpan lifetime) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        TokenHash = tokenHash,
        CreatedAt = now,
        ExpiresAt = now + lifetime,
    };
}
