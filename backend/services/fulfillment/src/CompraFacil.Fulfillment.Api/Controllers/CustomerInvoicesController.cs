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
        var owner = Guid.TryParse(User.FindFirstValue("sub"), out var sub) && invoice?.CustomerId == sub;
        // a rejeitada e a pendente não têm DANFE: para o cliente, ainda não existe nota
        if (invoice is null || !(owner || User.IsInRole("admin"))
            || invoice.Status is not (InvoiceStatus.Authorized or InvoiceStatus.Cancelled))
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Recurso não encontrado");
        }
        try
        {
            var pdf = await issuer.GetPdfAsync(invoice.ProviderId!, ct);
            return File(pdf, "application/pdf", $"nfe-{invoice.Number}.pdf");
        }
        catch (InvoiceIssuerUnavailableException)
        {
            Response.Headers.RetryAfter = "30";
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Serviço indisponível",
                detail: "o emissor da nota não respondeu; tente de novo em instantes");
        }
    }
}
