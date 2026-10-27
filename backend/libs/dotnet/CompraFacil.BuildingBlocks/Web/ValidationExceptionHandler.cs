using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.BuildingBlocks.Web;

/// <summary>
/// Converte a <see cref="ValidationException"/> do FluentValidation (lançada pelos casos de uso) no
/// 422 do contrato: ValidationProblem com um item por campo inválido, no caminho do JSON
/// (<c>items[0].quantity</c>).
/// </summary>
public sealed class ValidationExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (exception is not ValidationException validation)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Dados inválidos",
            Detail = "Um ou mais campos não passaram na validação.",
        };
        problem.Extensions["errors"] = validation.Errors
            .Select(e => new FieldError(FieldPath.ToSnakeCase(e.PropertyName), e.ErrorMessage))
            .ToList();

        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private sealed record FieldError(string Field, string Message);
}

/// <summary>Converte o caminho de propriedade do .NET para o do JSON do contrato.</summary>
public static class FieldPath
{
    /// <summary><c>Items[0].ProductId</c> → <c>items[0].product_id</c>.</summary>
    public static string ToSnakeCase(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var sb = new StringBuilder(path.Length + 8);
        foreach (var part in path.Split('.'))
        {
            if (sb.Length > 0)
            {
                sb.Append('.');
            }
            var bracket = part.IndexOf('[', StringComparison.Ordinal);
            var name = bracket < 0 ? part : part[..bracket];
            sb.Append(JsonNamingPolicy.SnakeCaseLower.ConvertName(name));
            if (bracket >= 0)
            {
                sb.Append(part.AsSpan(bracket));
            }
        }
        return sb.ToString();
    }
}
