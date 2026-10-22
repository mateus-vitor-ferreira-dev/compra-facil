using System.Diagnostics;
using CompraFacil.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace CompraFacil.BuildingBlocks.Web;

/// <summary>
/// Reaproveita o X-Request-ID recebido (ex.: do gateway) ou gera um novo, devolve no header da
/// resposta e o põe em <see cref="HttpContext.TraceIdentifier"/> (usado no ProblemDetails) e no
/// escopo de log. Ao fim, registra uma linha por requisição. É o mesmo comportamento do catalog.
/// </summary>
public sealed partial class RequestIdMiddleware(RequestDelegate next, ILogger<RequestIdMiddleware> logger)
{
    public const string Header = "X-Request-ID";
    private const int MaxLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var id = context.Request.Headers[Header].ToString();
        if (id.Length is 0 or > MaxLength)
        {
            id = Guid.NewGuid().ToString();
        }
        context.TraceIdentifier = id;
        // eventos publicados nesta requisição levam o request id como correlation_id
        CorrelationContext.Current = new CorrelationContext(id, CausationId: null);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["request_id"] = id }))
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                await next(context);
            }
            finally
            {
                var status = context.Response.StatusCode;
                var level = LevelFor(status);
                if (logger.IsEnabled(level))
                {
                    var latencyMs = Math.Round(Stopwatch.GetElapsedTime(start).TotalMilliseconds, 3);
                    var route = context.GetEndpoint() is RouteEndpoint e ? e.RoutePattern.RawText ?? "" : "";
                    var path = context.Request.Path.Value ?? "";
                    LogRequest(logger, level, context.Request.Method, route, path, status, latencyMs);
                }
            }
        }
    }

    private static LogLevel LevelFor(int status) => status switch
    {
        >= 500 => LogLevel.Error,
        >= 400 => LogLevel.Warning,
        _ => LogLevel.Information,
    };

    [LoggerMessage(Message = "http request {Method} {Route} {Path} {Status} {LatencyMs}ms")]
    private static partial void LogRequest(ILogger logger, LogLevel level, string method, string route, string path, int status, double latencyMs);
}
