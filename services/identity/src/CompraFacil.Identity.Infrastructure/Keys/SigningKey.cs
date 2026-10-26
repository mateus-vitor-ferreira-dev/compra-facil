using CompraFacil.Identity.Domain;

namespace CompraFacil.Identity.Infrastructure.Keys;

/// <summary>Uma chave RSA de assinatura no banco. A privada vai cifrada pelo Data Protection.</summary>
public sealed class SigningKey
{
    /// <summary>O <c>kid</c>: thumbprint da chave pública (RFC 7638).</summary>
    public string KeyId { get; private set; } = "";

    /// <summary>PKCS#8 cifrado (IDataProtector com o propósito <see cref="DbSigningKeyRing.ProtectorPurpose"/>).</summary>
    public byte[] ProtectedPrivateKey { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ActivatesAt { get; private set; }
    public DateTimeOffset RetiresAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    private SigningKey()
    {
    }

    public SigningKey(SigningKeyLifetime lifetime, byte[] protectedPrivateKey, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        KeyId = lifetime.KeyId;
        ProtectedPrivateKey = protectedPrivateKey;
        CreatedAt = now;
        ActivatesAt = lifetime.ActivatesAt;
        RetiresAt = lifetime.RetiresAt;
        ExpiresAt = lifetime.ExpiresAt;
    }

    public SigningKeyLifetime Lifetime => new(KeyId, ActivatesAt, RetiresAt, ExpiresAt);
}
