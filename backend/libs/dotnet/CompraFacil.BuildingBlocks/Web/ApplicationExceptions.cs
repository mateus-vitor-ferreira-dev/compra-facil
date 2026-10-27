using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.BuildingBlocks.Web;

/// <summary>
/// Erro de aplicação que vira um ProblemDetails com status definido. Os casos de uso lançam as
/// subclasses; o <see cref="ApplicationExceptionHandler"/> responde no formato do contrato. Com
/// <see cref="RetryAfter"/>, a resposta leva o header Retry-After (segundos, arredondado para cima).
/// </summary>
public abstract class ApplicationProblemException(int status, string title, string detail, TimeSpan? retryAfter = null) : Exception(detail)
{
    public int Status { get; } = status;
    public string Title { get; } = title;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>401: credencial ausente ou inválida (login, refresh token).</summary>
public sealed class UnauthorizedException(string detail)
    : ApplicationProblemException(StatusCodes.Status401Unauthorized, "Não autenticado", detail);

/// <summary>403: autenticado, mas sem permissão para a operação (ex.: e-mail ainda não confirmado).</summary>
public sealed class ForbiddenException(string title, string detail)
    : ApplicationProblemException(StatusCodes.Status403Forbidden, title, detail);

/// <summary>404: recurso inexistente.</summary>
public sealed class NotFoundException(string detail)
    : ApplicationProblemException(StatusCodes.Status404NotFound, "Recurso não encontrado", detail);

/// <summary>409: conflito com o estado atual (itens recusados, duplicidade, transição inválida).</summary>
public sealed class ConflictException(string title, string detail)
    : ApplicationProblemException(StatusCodes.Status409Conflict, title, detail);

/// <summary>503: dependência fora do ar; o cliente pode tentar de novo depois de <see cref="ApplicationProblemException.RetryAfter"/>.</summary>
public sealed class DependencyUnavailableException(string detail, TimeSpan retryAfter)
    : ApplicationProblemException(StatusCodes.Status503ServiceUnavailable, "Serviço indisponível", detail, retryAfter);

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
        if (problem.RetryAfter is { } retryAfter)
        {
            httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(null, null);
        }
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = problem.Status, Title = problem.Title, Detail = problem.Message },
            Exception = exception,
        });
    }
}
