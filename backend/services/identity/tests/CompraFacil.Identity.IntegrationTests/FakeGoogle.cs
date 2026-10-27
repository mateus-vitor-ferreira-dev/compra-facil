using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CompraFacil.Identity.IntegrationTests;

/// <summary>
/// Um Google de mentira: a discovery e o JWKS servidos por um HttpMessageHandler, e ID tokens assinados
/// pela chave dele. O identity valida como validaria os do Google de verdade.
/// </summary>
public sealed class FakeGoogle : IDisposable
{
    public const string Authority = "https://google.test";
    public const string ClientId = "compra-facil-teste";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RSA _other = RSA.Create(2048);

    private RsaSecurityKey Key => new(_rsa) { KeyId = "google-1" };

    public HttpMessageHandler Handler() => new Endpoints(this);

    /// <summary>Um ID token como o Google emitiria; os parâmetros estragam cada regra para os testes.</summary>
    public string IdToken(
        string sub, string email, string nonce, string? name = "Ana do Google", bool verified = true,
        string audience = ClientId, TimeSpan? expiresIn = null, bool otherKey = false)
    {
        var now = DateTime.UtcNow;
        var expires = now + (expiresIn ?? TimeSpan.FromMinutes(5));
        var claims = new Dictionary<string, object> { ["sub"] = sub, ["email"] = email, ["email_verified"] = verified, ["nonce"] = nonce };
        if (name is not null)
        {
            claims["name"] = name;
        }
        var key = otherKey ? new RsaSecurityKey(_other) { KeyId = "google-1" } : Key;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = audience,
            IssuedAt = expires < now ? expires.AddMinutes(-5) : now,
            NotBefore = expires < now ? expires.AddMinutes(-5) : now,
            Expires = expires,
            Claims = claims,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });
    }

    public void Dispose()
    {
        _rsa.Dispose();
        _other.Dispose();
    }

    private sealed class Endpoints(FakeGoogle google) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            object? body = path switch
            {
                "/.well-known/openid-configuration" => new Dictionary<string, object>
                {
                    ["issuer"] = Authority,
                    ["jwks_uri"] = $"{Authority}/oauth2/v3/certs",
                    ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
                },
                "/oauth2/v3/certs" => new { keys = new[] { JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(google._rsa.ExportParameters(false)) { KeyId = "google-1" }) } },
                _ => null,
            };
            var response = body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web)) };
            return Task.FromResult(response);
        }
    }
}
