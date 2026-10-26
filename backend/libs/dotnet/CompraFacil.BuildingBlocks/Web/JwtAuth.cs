using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace CompraFacil.BuildingBlocks.Web;

/// <summary>Tokens aceitos (seção Auth): os do identity, validados pelas chaves públicas dele.</summary>
public sealed class JwtAuthOptions
{
    public const string Section = "Auth";

    /// <summary>Endereço do identity (ex.: http://identity:8006): descoberta e JWKS em /.well-known/.</summary>
    public string? Authority { get; set; }

    public string Issuer { get; set; } = "compra-facil-identity";
    public string Audience { get; set; } = "compra-facil";
    public TimeSpan Leeway { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Exige HTTPS para buscar as chaves. Sem valor: só fora de Development. O compose e o k3d desligam,
    /// porque o identity está na rede interna, sem TLS.
    /// </summary>
    public bool? RequireHttpsMetadata { get; set; }

    /// <summary>Intervalo mínimo entre duas buscas do JWKS por causa de um <c>kid</c> desconhecido.</summary>
    public TimeSpan KeyRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>JWKS fixo (o JSON de /.well-known/jwks.json), no lugar do Authority: testes, sem HTTP.</summary>
    public string? Jwks { get; set; }
}

/// <summary>
/// JWT do identity (RS256, iss compra-facil-identity, aud compra-facil) nas rotas protegidas. As chaves
/// vêm do JWKS do identity, guardadas em memória: renovadas de hora em hora e na chegada de um token com
/// <c>kid</c> desconhecido (rotação), no máximo a cada <see cref="JwtAuthOptions.KeyRefreshInterval"/>. O identity fora do ar não derruba o serviço;
/// só as rotas protegidas respondem 401 até as chaves chegarem.
/// Sem token: 401; token sem o papel: 403 — ambos em ProblemDetails.
/// </summary>
public static class JwtAuth
{
    /// <summary>Operador: rotas /v1/admin/*.</summary>
    public const string AdminPolicy = "admin";

    /// <summary>Cliente: o que a loja faz em nome de quem está logado (pedido, "meus pedidos").</summary>
    public const string CustomerPolicy = "customer";

    public static WebApplicationBuilder AddJwtAuth(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new JwtAuthOptions();
        builder.Configuration.GetSection(JwtAuthOptions.Section).Bind(options);
        var requireHttps = options.RequireHttpsMetadata ?? !builder.Environment.IsDevelopment();
        var jwks = string.IsNullOrWhiteSpace(options.Jwks) ? null : new JsonWebKeySet(options.Jwks);
        var metadataAddress = jwks is null ? MetadataAddress(options, requireHttps) : null;

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false; // mantém "sub", "email" e "roles" como vieram
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = options.Issuer,
                ValidAudience = options.Audience,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256], // barra alg=none e HS256 com a chave pública
                RequireExpirationTime = true,
                ClockSkew = options.Leeway,
                NameClaimType = "sub",
                RoleClaimType = "roles",
            };

            if (jwks is not null)
            {
                o.TokenValidationParameters.IssuerSigningKeys = jwks.GetSigningKeys();
                return;
            }
            o.MetadataAddress = metadataAddress!; // sem Jwks, já validado acima
            o.RequireHttpsMetadata = requireHttps;
            o.BackchannelTimeout = TimeSpan.FromSeconds(5);
            // a próxima chave aparece no JWKS um dia antes de assinar: 1 h de cache sobra
            o.AutomaticRefreshInterval = TimeSpan.FromHours(1);
            o.RefreshInterval = options.KeyRefreshInterval;
        });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, p => p.RequireAuthenticatedUser().RequireClaim("sub").RequireRole("admin"))
            .AddPolicy(CustomerPolicy, p => p.RequireAuthenticatedUser().RequireClaim("sub").RequireRole("customer"));
        return builder;
    }

    // validado na subida: configuração errada derruba o serviço já, não na primeira requisição autenticada
    private static string MetadataAddress(JwtAuthOptions options, bool requireHttps)
    {
        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority) || authority.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"Auth:Authority deve ser a URL do identity (ex.: http://identity:8006), não '{options.Authority}'");
        }
        if (requireHttps && authority.Scheme != "https")
        {
            throw new InvalidOperationException(
                $"Auth:Authority {authority} sem HTTPS; na rede interna, defina Auth__RequireHttpsMetadata=false");
        }
        return authority.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration";
    }
}
