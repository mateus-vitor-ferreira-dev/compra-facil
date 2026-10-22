using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompraFacil.BuildingBlocks.Web;

/// <summary>Identifica o serviço para os padrões comuns.</summary>
/// <param name="Name">Nome curto (orders, inventory...), usado nos logs.</param>
/// <param name="ContractFile">
/// Arquivo do contrato em contracts/openapi (ex.: orders.v1.yaml), servido em /openapi e exibido em
/// /docs. Nulo para serviço sem API pública (notifications).
/// </param>
public sealed record ServiceInfo(string Name, string? ContractFile = null);

public static class ServiceDefaults
{
    /// <summary>Tag dos health checks que entram no /ready (dependências obrigatórias).</summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// Registra o que todo serviço .NET da CompraFácil tem igual: controllers com JSON em
    /// snake_case, erros em ProblemDetails (RFC 9457) com request_id, validação do FluentValidation
    /// como 422, logs JSON e health checks.
    /// </summary>
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, ServiceInfo service)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton(service);

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(o =>
        {
            o.IncludeScopes = true; // request_id entra por escopo (RequestIdMiddleware)
            o.UseUtcTimestamp = true;
            o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });

        builder.Services
            .AddControllers()
            .AddJsonOptions(o => ConfigureJson(o.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = Problems.MalformedRequest);
        builder.Services.ConfigureHttpJsonOptions(o => ConfigureJson(o.SerializerOptions));

        builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = Problems.Customize);
        builder.Services.AddExceptionHandler<ValidationExceptionHandler>();

        builder.Services.AddHealthChecks();
        return builder;
    }

    /// <summary>Pipeline comum: request id, tratamento de erros, health, docs do contrato.</summary>
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseMiddleware<RequestIdMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages(); // 404/405 sem corpo viram ProblemDetails

        // liveness não olha dependências: só diz que o processo responde
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) });

        var service = app.Services.GetRequiredService<ServiceInfo>();
        if (service.ContractFile is not null)
        {
            ContractDocs.Map(app, service);
        }
        return app;
    }

    /// <summary>JSON dos contratos: snake_case, enums como texto snake_case, sem nulos de sobra.</summary>
    public static void ConfigureJson(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    }
}

/// <summary>Atalho para os controllers devolverem ProblemDetails no formato combinado.</summary>
public static class Problems
{
    // títulos padrão em português, como no catalog; título definido pelo código é mantido
    private static readonly Dictionary<int, string> s_titles = new()
    {
        [StatusCodes.Status400BadRequest] = "Requisição malformada",
        [StatusCodes.Status401Unauthorized] = "Não autenticado",
        [StatusCodes.Status403Forbidden] = "Sem permissão",
        [StatusCodes.Status404NotFound] = "Recurso não encontrado",
        [StatusCodes.Status405MethodNotAllowed] = "Método não permitido",
        [StatusCodes.Status409Conflict] = "Conflito",
        [StatusCodes.Status415UnsupportedMediaType] = "Tipo de conteúdo não suportado",
        [StatusCodes.Status500InternalServerError] = "Erro interno",
        [StatusCodes.Status503ServiceUnavailable] = "Serviço indisponível",
    };

    /// <summary>
    /// Ajusta todo ProblemDetails ao formato do contrato: type about:blank, instance, título em
    /// português e request_id. O ASP.NET ainda acrescenta trace_id (W3C), mantido de propósito: liga
    /// o erro ao trace no Grafana Tempo.
    /// </summary>
    internal static void Customize(ProblemDetailsContext ctx)
    {
        var problem = ctx.ProblemDetails;
        // o ASP.NET preenche type com um link da RFC; o contrato usa about:blank sem tipo específico
        if (problem.Type is null || problem.Type.StartsWith("https://tools.ietf.org/", StringComparison.Ordinal))
        {
            problem.Type = "about:blank";
        }
        var status = problem.Status ?? ctx.HttpContext.Response.StatusCode;
        if (IsAspNetDefaultTitle(problem.Title, status) && s_titles.TryGetValue(status, out var title))
        {
            problem.Title = title;
        }
        problem.Instance ??= ctx.HttpContext.Request.Path;
        problem.Extensions["request_id"] = ctx.HttpContext.TraceIdentifier;
    }

    // títulos que o próprio ASP.NET põe: a reason phrase ou, no exception handler, esta frase
    private static bool IsAspNetDefaultTitle(string? title, int status) =>
        title is null
        || title == ReasonPhrases.GetReasonPhrase(status)
        || title == "An error occurred while processing your request.";

    /// <summary>400 do contrato (BadRequest): JSON inválido ou parâmetro com tipo errado.</summary>
    internal static IActionResult MalformedRequest(ActionContext context)
    {
        var details = context.ModelState
            .Where(e => e.Value is { Errors.Count: > 0 })
            .Select(e => $"{(e.Key.Length == 0 ? "corpo" : FieldPath.ToSnakeCase(e.Key))}: {e.Value!.Errors[0].ErrorMessage}");
        // pela factory: aplica Customize (about:blank, instance, request_id)
        var problem = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>()
            .CreateProblemDetails(context.HttpContext, StatusCodes.Status400BadRequest,
                title: "Requisição malformada", detail: string.Join("; ", details));
        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }
}
