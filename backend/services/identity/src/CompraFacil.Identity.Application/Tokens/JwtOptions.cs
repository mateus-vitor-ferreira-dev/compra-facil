using CompraFacil.Identity.Domain;

namespace CompraFacil.Identity.Application.Tokens;

/// <summary>Emissão dos tokens (seção Identity:Tokens).</summary>
public sealed class JwtOptions
{
    public const string Section = "Identity:Tokens";

    /// <summary>Claim <c>iss</c>: os outros serviços aceitam só tokens deste emissor.</summary>
    public string Issuer { get; set; } = "compra-facil-identity";

    /// <summary>Claim <c>aud</c>: um token serve para todas as APIs da loja.</summary>
    public string Audience { get; set; } = "compra-facil";

    public TimeSpan AccessTokenLifetime { get; set; } = KeyRotationPolicy.Default.TokenLifetime;
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Por quanto tempo, depois da troca, o refresh token anterior ainda dá um par novo da mesma sessão em
    /// vez de derrubá-la: requisições paralelas do mesmo navegador (a página e um prefetch, em instâncias
    /// diferentes do BFF) renovam o mesmo token. Fora dele, reuso derruba a sessão. Zero desliga.
    /// </summary>
    public TimeSpan RefreshReuseInterval { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan KeyRotateEvery { get; set; } = KeyRotationPolicy.Default.RotateEvery;
    public TimeSpan KeyPublishAhead { get; set; } = KeyRotationPolicy.Default.PublishAhead;

    public KeyRotationPolicy RotationPolicy => new(KeyRotateEvery, KeyPublishAhead, AccessTokenLifetime);
}
