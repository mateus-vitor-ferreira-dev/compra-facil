using CompraFacil.Identity.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CompraFacil.Identity.Application.Tokens;

/// <summary>
/// Emite o access token: JWT RS256 com <c>sub</c> (id da conta), <c>email</c>, <c>name</c>,
/// <c>roles</c> e <c>jti</c>, assinado pela chave atual (o <c>kid</c> no header aponta a chave no JWKS).
/// </summary>
public sealed class AccessTokenIssuer(ISigningKeyRing keys, IOptions<JwtOptions> options, TimeProvider clock)
{
    private static readonly JsonWebTokenHandler s_handler = new() { SetDefaultTimesOnTokenCreation = false };

    public async Task<string> IssueAsync(User user, IReadOnlyCollection<string> roles, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        var set = await keys.CurrentAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        return s_handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + options.Value.AccessTokenLifetime,
            SigningCredentials = set.Signing,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email!,
                [JwtRegisteredClaimNames.Name] = user.Name,
                ["roles"] = roles.Order(StringComparer.Ordinal).ToArray(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
        });
    }
}
