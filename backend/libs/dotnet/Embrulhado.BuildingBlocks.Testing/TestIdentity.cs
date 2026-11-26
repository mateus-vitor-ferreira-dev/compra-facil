using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Embrulhado.BuildingBlocks.Testing;

/// <summary>
/// Faz o papel do identity nos testes: uma chave RSA, o JWKS dela (para <c>Auth:Jwks</c>, sem HTTP) e
/// tokens no formato dos de verdade (RS256, kid, iss, aud, sub, email, roles, exp). O token de operador sai
/// com <c>amr</c> = <c>mfa</c>, como o do identity depois do segundo fator.
/// </summary>
public sealed class TestIdentity : IDisposable
{
    public const string Issuer = "embrulhado-identity";
    public const string Audience = "embrulhado";

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

    public string AdminToken() => Token(["admin"], amr: ["mfa"]);

    /// <summary>O operador que só passou pela senha (sem <c>amr</c>): as rotas de operador recusam (ameaça T8).</summary>
    public string AdminTokenWithoutMfa() => Token(["admin"]);

    /// <summary>O papel só de leitura da operação (#403).</summary>
    public string AuditorToken() => Token(["auditor"]);

    public string CustomerToken(Guid? customerId = null, string email = "ana@exemplo.com") =>
        Token(["customer"], subject: (customerId ?? Guid.NewGuid()).ToString(), email: email);

    public string Token(
        string[] roles, string? subject = "0199f6a0-0000-7000-8000-000000000a1a", string email = "ana@exemplo.com",
        DateTime? expires = null, string issuer = Issuer, string audience = Audience, string[]? amr = null)
    {
        var claims = new Dictionary<string, object> { ["roles"] = roles, ["email"] = email };
        if (amr is not null)
        {
            claims["amr"] = amr;
        }
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
