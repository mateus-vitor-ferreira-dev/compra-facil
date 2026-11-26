namespace Embrulhado.Identity.Api;

public static class LowercaseApiPaths
{
    /// <summary>
    /// 404 para caminho de /v1/... com maiúscula ou fora do ASCII. O roteamento do ASP.NET não diferencia
    /// maiúsculas (e não tem opção para diferenciar), mas o gateway diferencia: POST /v1/Sessions chegava ao
    /// login por uma rota do gateway sem o rate limit de senha. Os contratos só têm caminhos em minúsculas
    /// (regra do Spectral), então nenhuma chamada válida cai aqui. /health, /ready, /docs, /openapi e
    /// /.well-known ficam de fora.
    /// </summary>
    public static WebApplication UseLowercaseApiPaths(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;
            if (path is not null && path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase)
                && path.Any(c => !char.IsAscii(c) || char.IsAsciiLetterUpper(c)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound; // o UseStatusCodePages faz o ProblemDetails
                return;
            }
            await next(context);
        });
        return app;
    }
}
