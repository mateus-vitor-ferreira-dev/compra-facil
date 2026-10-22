using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.BuildingBlocks.Web;

/// <summary>
/// Erro de aplicação que vira um ProblemDetails com status definido. Os casos de uso lançam as
/// subclasses; o <see cref="ApplicationExceptionHandler"/> responde no formato do contrato.
/// </summary>
public abstract class ApplicationProblemException(int status, string title, string detail) : Exception(detail)
{
    public int Status { get; } = status;
    public string Title { get; } = title;
}

/// <summary>404: recurso inexistente.</summary>
public sealed class NotFoundException(string detail)
    : ApplicationProblemException(StatusCodes.Status404NotFound, "Recurso não encontrado", detail);

/// <summary>409: conflito com o estado atual (itens recusados, duplicidade, transição inválida).</summary>
public sealed class ConflictException(string title, string detail)
    : ApplicationProblemException(StatusCodes.Status409Conflict, title, detail);

/// <summary>503: dependência fora do ar; o cliente pode tentar de novo depois de <see cref="RetryAfter"/>.</summary>
public sealed class DependencyUnavailableException(string detail, TimeSpan retryAfter)
    : ApplicationProblemException(StatusCodes.Status503ServiceUnavailable, "Serviço indisponível", detail)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

public sealed class ApplicationExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (exception is not ApplicationProblemException problem)
        {
            return false;
        }
        httpContext.Response.StatusCode = problem.Status;
        if (problem is DependencyUnavailableException unavailable)
        {
            httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(unavailable.RetryAfter.TotalSeconds)).ToString(null, null);
        }
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = problem.Status, Title = problem.Title, Detail = problem.Message },
            Exception = exception,
        });
    }
}
