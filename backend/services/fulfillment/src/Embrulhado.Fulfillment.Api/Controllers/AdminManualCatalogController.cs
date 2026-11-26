using Embrulhado.BuildingBlocks.Web;
using Embrulhado.Fulfillment.Application.ManualSupplier;
using Embrulhado.Fulfillment.Application.SupplierCatalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Embrulhado.Fulfillment.Api.Controllers;

/// <summary>
/// importSupplierSheet e getSupplierSheet (contracts/openapi/fulfillment.v1.yaml): a planilha do fornecedor manual
/// (#412). Subir é do operador; ver quando foi a última, também do auditor.
/// </summary>
[ApiController]
[Produces("application/json")]
[Authorize(Policy = JwtAuth.AdminReadPolicy)]
public sealed class AdminManualCatalogController(
    ManualCatalogImport import, IManualCatalogRepository sheet, ManualSupplierMode mode, IProblemDetailsService problems) : ControllerBase
{
    /// <summary>O CSV no corpo (text/csv) ou como o arquivo "file" de um formulário (multipart/form-data).</summary>
    [HttpPost("v1/admin/supplier-catalog/sheet")]
    [Authorize(Policy = JwtAuth.AdminPolicy)]
    [RequestSizeLimit(ManualCatalogSheet.MaxBytes + 64 * 1024)]
    [Consumes("text/csv", "multipart/form-data")]
    public async Task<ActionResult<ManualImportResponse>> Import(CancellationToken ct)
    {
        byte[] csv;
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(ct);
            if (form.Files.GetFile("file") is not { } file)
            {
                return await SheetProblemAsync([new SheetError(1, "envie a planilha no campo file")]);
            }
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            csv = buffer.ToArray();
        }
        else
        {
            using var buffer = new MemoryStream();
            await Request.Body.CopyToAsync(buffer, ct);
            csv = buffer.ToArray();
        }
        try
        {
            var result = await import.ImportAsync(csv, ct);
            return result.Errors.Count > 0 ? await SheetProblemAsync(result.Errors) : Ok(new ManualImportResponse(result.Products, result.Sync!));
        }
        catch (ManualSupplierDisabledException e)
        {
            throw new ConflictException("Fornecedor fora do modo manual", e.Message);
        }
        catch (CatalogSyncBusyException e)
        {
            throw new ConflictException("Sincronização em andamento", e.Message);
        }
    }

    /// <summary>422 com um erro por linha (SheetProblem): nada mudou.</summary>
    private async Task<EmptyResult> SheetProblemAsync(IReadOnlyList<SheetError> errors)
    {
        HttpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status422UnprocessableEntity,
            Title = "Planilha com erro",
            Detail = $"{errors.Count} {(errors.Count == 1 ? "linha precisa" : "linhas precisam")} de correção; nada mudou na loja.",
        };
        problem.Extensions["sheet_errors"] = errors.Select(e => new { line = e.Line, message = e.Message }).ToList();
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = HttpContext, ProblemDetails = problem });
        return new EmptyResult();
    }

    /// <summary>Se o modo manual está ligado, quantos produtos tem a planilha e quando ela foi subida.</summary>
    [HttpGet("v1/admin/supplier-catalog/sheet")]
    public async Task<ManualSheetSummary> Summary(CancellationToken ct) =>
        mode.Enabled
            ? new ManualSheetSummary(true, (await sheet.ListAsync(ct)).Count, await sheet.LastImportAtAsync(ct))
            : new ManualSheetSummary(false, 0, null);

    /// <summary>O modelo do CSV, com o cabeçalho e uma linha de exemplo.</summary>
    [HttpGet("v1/admin/supplier-catalog/sheet/template")]
    [Produces("text/csv")]
    public FileContentResult Template() =>
        File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(ManualCatalogSheet.Template)).ToArray(),
            "text/csv; charset=utf-8", "modelo-fornecedor.csv");
}

public sealed record ManualImportResponse(int Products, CatalogSyncResult Sync);

public sealed record ManualSheetSummary(bool Enabled, int Products, DateTimeOffset? ImportedAt);
