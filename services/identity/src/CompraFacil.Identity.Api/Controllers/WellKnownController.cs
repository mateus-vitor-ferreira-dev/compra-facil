using System.Text.Json.Serialization;
using CompraFacil.Identity.Application.Tokens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CompraFacil.Identity.Api.Controllers;

/// <summary>
/// Descoberta (OpenID Connect Discovery, só o que a validação usa) e as chaves públicas (JWKS, RFC 7517).
/// Os outros serviços validam o JWT sozinhos com elas, sem chamar o identity a cada requisição.
/// </summary>
[ApiController]
[Route(".well-known")]
[Produces("application/json")]
public sealed class WellKnownController(ISigningKeyRing keys, IOptions<JwtOptions> options) : ControllerBase
{
    // a próxima chave entra no JWKS um dia antes de assinar (KeyPublishAhead): 5 min de cache sobra
    private const string CacheFor = "public, max-age=300";

    /// <summary>getJwks: as chaves públicas em vigor (a anterior, a atual e a próxima).</summary>
    [HttpGet("jwks.json")]
    public async Task<JwksResponse> Jwks(CancellationToken ct)
    {
        var set = await keys.CurrentAsync(ct);
        Response.Headers.CacheControl = CacheFor;
        return new JwksResponse([.. set.Published.Select(Jwk.From)]);
    }

    /// <summary>getOpenIdConfiguration: emissor e onde ficam as chaves (para o JwtBearer achar o JWKS sozinho).</summary>
    [HttpGet("openid-configuration")]
    public OpenIdConfiguration Discovery()
    {
        Response.Headers.CacheControl = CacheFor;
        return new OpenIdConfiguration(options.Value.Issuer, $"{BaseUrl()}/.well-known/jwks.json", [SecurityAlgorithms.RsaSha256]);
    }

    // o endereço como quem chamou o vê: direto (http://identity:8006, os outros serviços) ou pelo gateway,
    // que tira o /identity do caminho e o devolve em X-Forwarded-Prefix (stripPrefix do Traefik)
    private string BaseUrl()
    {
        var scheme = Request.Headers["X-Forwarded-Proto"].FirstOrDefault() is "https" ? "https" : Request.Scheme;
        var prefix = Request.Headers["X-Forwarded-Prefix"].FirstOrDefault() is { Length: > 0 } p && p.StartsWith('/') ? p.TrimEnd('/') : "";
        return $"{scheme}://{Request.Host}{prefix}{Request.PathBase}";
    }
}

public sealed record JwksResponse(IReadOnlyList<Jwk> Keys);

/// <summary>Chave pública RSA no formato JWK (RFC 7517/7518).</summary>
public sealed record Jwk(
    [property: JsonPropertyName("kty")] string Kty,
    [property: JsonPropertyName("use")] string Use,
    [property: JsonPropertyName("alg")] string Alg,
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("n")] string N,
    [property: JsonPropertyName("e")] string E)
{
    public static Jwk From(RsaSecurityKey key)
    {
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(key);
        return new("RSA", "sig", SecurityAlgorithms.RsaSha256, key.KeyId, jwk.N, jwk.E);
    }
}

public sealed record OpenIdConfiguration(
    string Issuer,
    string JwksUri,
    IReadOnlyList<string> IdTokenSigningAlgValuesSupported);
