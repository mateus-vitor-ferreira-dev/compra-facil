using System.Security.Claims;
using CompraFacil.Fulfillment.Application.Invoices;
using CompraFacil.Fulfillment.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Fulfillment.Api.Controllers;

/// <summary>
/// getInvoiceDanfe (contracts/openapi/fulfillment.v1.yaml): o DANFE da nota do pedido, em PDF, para o cliente
/// baixar (#221). Só o dono do pedido ou a equipe; a nota de outro cliente dá 404, como se não existisse.
/// </summary>
[ApiController]
[Authorize]
public sealed class CustomerInvoicesController(IInvoiceRepository invoices, IInvoiceIssuer issuer) : ControllerBase
{
    [HttpGet("v1/me/invoices/{order_id:guid}/danfe")]
    [Produces("application/pdf", "application/problem+json")]
    public async Task<IActionResult> Danfe([FromRoute(Name = "order_id")] Guid orderId, CancellationToken ct)
    {
        var invoice = await invoices.GetAsync(orderId, ct);
        // a rejeitada e a pendente não têm DANFE: para o cliente, ainda não existe nota
        var issued = invoice?.Status is InvoiceStatus.Authorized or InvoiceStatus.Cancelled;
        return await PdfAsync(invoice, issued ? invoice!.ProviderId : null, invoice?.Number, ct);
    }

    /// <summary>getReturnInvoiceDanfe: o DANFE da nota de devolução (#223); enquanto ela não sai, 404.</summary>
    [HttpGet("v1/me/invoices/{order_id:guid}/return-danfe")]
    [Produces("application/pdf", "application/problem+json")]
    public async Task<IActionResult> ReturnDanfe([FromRoute(Name = "order_id")] Guid orderId, CancellationToken ct)
    {
        var invoice = await invoices.GetAsync(orderId, ct);
        return await PdfAsync(invoice, invoice?.ReturnAccessKey is null ? null : invoice.ReturnProviderId, invoice?.ReturnNumber, ct);
    }

    private async Task<IActionResult> PdfAsync(Invoice? invoice, string? providerId, int? number, CancellationToken ct)
    {
        var owner = Guid.TryParse(User.FindFirstValue("sub"), out var sub) && invoice?.CustomerId == sub;
        if (providerId is null || !(owner || User.IsInRole("admin")))
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Recurso não encontrado");
        }
        try
        {
            var pdf = await issuer.GetPdfAsync(providerId, ct);
            return File(pdf, "application/pdf", $"nfe-{number}.pdf");
        }
        catch (InvoiceIssuerUnavailableException)
        {
            Response.Headers.RetryAfter = "30";
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Serviço indisponível",
                detail: "o emissor da nota não respondeu; tente de novo em instantes");
        }
    }
}
