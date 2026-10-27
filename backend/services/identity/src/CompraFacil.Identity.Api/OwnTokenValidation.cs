using CompraFacil.Identity.Application.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CompraFacil.Identity.Api;

public static class OwnTokenValidation
{
    /// <summary>
    /// JwtBearer com as chaves do próprio anel (as mesmas do JWKS) e as mesmas regras que os outros
    /// serviços aplicam: RS256, emissor e audiência do identity, <c>exp</c> obrigatório.
    /// </summary>
    public static WebApplicationBuilder AddOwnTokenValidation(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<ISigningKeyRing, IOptions<JwtOptions>, TimeProvider>((o, keys, tokens, clock) =>
            {
                o.MapInboundClaims = false; // mantém "sub" e "roles" como vieram
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = tokens.Value.Issuer,
                    ValidAudience = tokens.Value.Audience,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256], // barra alg=none e HS256 com a chave pública
                    IssuerSigningKeyResolver = (_, _, kid, _) => keys.PublishedSnapshot().Where(k => k.KeyId == kid),
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    // o validador padrão usa DateTime.UtcNow; este, o relógio do serviço (o mesmo que emite)
                    LifetimeValidator = (notBefore, expires, _, p) =>
                    {
                        var now = clock.GetUtcNow().UtcDateTime;
                        return expires is { } exp && exp > now - p.ClockSkew && (notBefore is not { } nbf || nbf <= now + p.ClockSkew);
                    },
                    NameClaimType = "sub",
                    RoleClaimType = "roles",
                };
            });
        return builder;
    }

    /// <summary>
    /// GET /problems/{status}: o rate limit do Traefik responde texto puro; o middleware errors do
    /// gateway busca aqui o ProblemDetails do contrato (como o catalog faz para a vitrine).
    /// </summary>
    public static void MapGatewayProblems(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGet("/problems/{status:int}", (int status, HttpResponse response) =>
            {
                if (status != StatusCodes.Status429TooManyRequests)
                {
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound);
                }
                response.Headers.RetryAfter = "1"; // o limite é por segundo
                return Results.Problem(statusCode: status, title: "Muitas requisições",
                    detail: "limite de requisições por IP excedido; tente de novo em instantes");
            })
            .ExcludeFromDescription();
    }
}
