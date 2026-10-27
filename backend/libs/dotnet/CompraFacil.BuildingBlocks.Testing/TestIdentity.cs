using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CompraFacil.BuildingBlocks.Testing;

/// <summary>
/// Faz o papel do identity nos testes: uma chave RSA, o JWKS dela (para <c>Auth:Jwks</c>, sem HTTP) e
/// tokens no formato dos de verdade (RS256, kid, iss, aud, sub, email, roles, exp).
/// </summary>
public sealed class TestIdentity : IDisposable
{
    public const string Issuer = "compra-facil-identity";
    public const string Audience = "compra-facil";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;

    public TestIdentity()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "teste-" + Guid.NewGuid().ToString("N")[..8] };
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = _key.KeyId });
        Jwks = JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid = _key.KeyId, n = jwk.N, e = jwk.E } } });
    }

    /// <summary>O JSON de /.well-known/jwks.json: vai em <c>Auth:Jwks</c>.</summary>
    public string Jwks { get; }

    public string AdminToken() => Token(["admin"]);

    public string CustomerToken(Guid? customerId = null, string email = "ana@exemplo.com") =>
        Token(["customer"], subject: (customerId ?? Guid.NewGuid()).ToString(), email: email);

    public string Token(
        string[] roles, string? subject = "0199f6a0-0000-7000-8000-000000000a1a", string email = "ana@exemplo.com",
        DateTime? expires = null, string issuer = Issuer, string audience = Audience)
    {
        var claims = new Dictionary<string, object> { ["roles"] = roles, ["email"] = email };
        if (subject is not null)
        {
            claims["sub"] = subject;
        }
        var exp = expires ?? DateTime.UtcNow.AddMinutes(15);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = exp.AddMinutes(-15),
            NotBefore = exp.AddMinutes(-15),
            Expires = exp,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
    }

    public void Dispose() => _rsa.Dispose();
}
