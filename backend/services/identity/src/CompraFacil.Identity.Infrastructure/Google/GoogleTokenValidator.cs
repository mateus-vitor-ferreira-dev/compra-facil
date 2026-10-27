using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Identity.Application.Accounts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CompraFacil.Identity.Infrastructure.Google;

/// <summary>
/// Valida o ID token do Google: assinatura pelas chaves da discovery (guardadas e renovadas pelo
/// ConfigurationManager), emissor do Google, <c>aud</c> igual a um dos client ids, validade, o
/// <c>nonce</c> que o BFF gerou para este login e o e-mail verificado pelo Google.
/// </summary>
internal sealed class GoogleTokenValidator : IGoogleTokenValidator
{
    public const string HttpClientName = "google";

    private readonly GoogleOptions _options;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configuration;
    private readonly JsonWebTokenHandler _handler = new();

    public GoogleTokenValidator(IOptions<GoogleOptions> options, IHttpClientFactory http)
    {
        _options = options.Value;
        var authority = _options.Authority.TrimEnd('/');
        _configuration = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(http.CreateClient(HttpClientName)) { RequireHttps = authority.StartsWith("https://", StringComparison.Ordinal) });
    }

    public async Task<GoogleIdentity> ValidateAsync(string idToken, string nonce, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            throw new UnauthorizedException("o login com o Google não está configurado");
        }
        if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(nonce))
        {
            throw new UnauthorizedException("ID token ou nonce ausente");
        }
        var config = await _configuration.GetConfigurationAsync(ct);
        var authority = _options.Authority.TrimEnd('/');
        var result = await _handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            // o Google emite com e sem o esquema
            ValidIssuers = [authority, new Uri(authority).Host],
            ValidAudiences = _options.ClientIds,
            IssuerSigningKeys = config.SigningKeys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        });
        if (!result.IsValid)
        {
            throw new UnauthorizedException("ID token do Google inválido");
        }
        var claims = result.Claims;
        string? Claim(string name) => claims.TryGetValue(name, out var v) ? v?.ToString() : null;
        if (Claim("nonce") != nonce)
        {
            throw new UnauthorizedException("o ID token não é deste login (nonce)");
        }
        if (!string.Equals(Claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedException("o Google não verificou este e-mail");
        }
        var sub = Claim("sub");
        var email = Claim("email");
        if (string.IsNullOrWhiteSpace(sub) || string.IsNullOrWhiteSpace(email))
        {
            throw new UnauthorizedException("ID token sem sub ou e-mail");
        }
        var name = Claim("name");
        return new GoogleIdentity(sub, email, string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name);
    }
}
