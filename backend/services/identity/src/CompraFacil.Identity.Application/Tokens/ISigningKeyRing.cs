using Microsoft.IdentityModel.Tokens;

namespace CompraFacil.Identity.Application.Tokens;

/// <summary>As chaves agora: a que assina e as públicas que vão no JWKS (anterior, atual e próxima).</summary>
public sealed record SigningKeySet(SigningCredentials Signing, IReadOnlyList<RsaSecurityKey> Published);

/// <summary>Chaves RSA de assinatura dos JWTs, com rotação (ver <see cref="Domain.SigningKeySchedule"/>).</summary>
public interface ISigningKeyRing
{
    /// <summary>Carrega (e cria ou rotaciona, se for a hora) as chaves; cache curto em memória.</summary>
    Task<SigningKeySet> CurrentAsync(CancellationToken ct);

    /// <summary>
    /// As chaves públicas da última carga, sem I/O: o validador de JWT do próprio identity é síncrono.
    /// Vazio antes da primeira carga (o Program carrega na subida).
    /// </summary>
    IReadOnlyList<SecurityKey> PublishedSnapshot();
}
