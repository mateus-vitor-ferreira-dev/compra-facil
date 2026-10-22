using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace CompraFacil.BuildingBlocks.Web;

/// <summary>Configuração do JWT dos operadores (seção Auth), igual à do catalog.</summary>
public sealed class AdminAuthOptions
{
    public string JwtSecret { get; set; } = AdminAuth.DevJwtSecret;
    public string Issuer { get; set; } = "compra-facil";
    public TimeSpan Leeway { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Aceita o segredo de dev fora de Development (no compose). Nunca em produção.</summary>
    public bool AllowDevSecret { get; set; }
}

/// <summary>
/// Rotas /v1/admin/* com o mesmo JWT do catalog (HS256, iss compra-facil, roles com "admin",
/// sub e exp obrigatórios). A audiência é o nome do serviço: gere com `task token -- -aud inventory`.
/// Sem token: 401; token sem a role: 403 — ambos em ProblemDetails.
/// </summary>
public static class AdminAuth
{
    public const string Policy = "admin";
    public const string Role = "admin";

    /// <summary>Segredo público do ambiente local (o mesmo do catalog); barrado fora dele.</summary>
    public const string DevJwtSecret = "dev-only-compra-facil-jwt-secret-troque-em-producao";

    private const int MinSecretLength = 32;

    public static WebApplicationBuilder AddAdminAuth(this WebApplicationBuilder builder, string audience)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new AdminAuthOptions();
        builder.Configuration.GetSection("Auth").Bind(options);
        if (options.JwtSecret.Length < MinSecretLength)
        {
            throw new InvalidOperationException($"Auth:JwtSecret precisa de pelo menos {MinSecretLength} caracteres");
        }
        if (options.JwtSecret == DevJwtSecret && !builder.Environment.IsDevelopment() && !options.AllowDevSecret)
        {
            throw new InvalidOperationException(
                $"o segredo JWT de desenvolvimento não pode ser usado em {builder.Environment.EnvironmentName}; defina Auth__JwtSecret");
        }

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false; // mantém "roles" e "sub" como vieram
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = options.Issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.JwtSecret)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], // barra alg=none e troca de algoritmo
                RequireExpirationTime = true,
                ClockSkew = options.Leeway,
                NameClaimType = "sub",
                RoleClaimType = "roles",
            };
        });
        builder.Services.AddAuthorizationBuilder().AddPolicy(Policy, p => p
            .RequireAuthenticatedUser()
            .RequireClaim("sub")
            .RequireRole(Role));
        return builder;
    }
}
