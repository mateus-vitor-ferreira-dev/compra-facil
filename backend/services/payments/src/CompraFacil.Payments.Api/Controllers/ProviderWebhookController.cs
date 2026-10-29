using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Payments.Application.Payments;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Payments.Api.Controllers;

/// <summary>
/// receivePaymentNotification (contracts/openapi/payments.v1.yaml): o provedor avisa que uma cobrança
/// mudou, no formato do Mercado Pago (?data.id=&amp;type=payment, x-signature). A assinatura é conferida e a
/// cobrança é consultada nele: o corpo não é confiável sozinho.
/// </summary>
[ApiController]
[Route("v1/webhooks/payments")]
[Produces("application/json")]
public sealed class ProviderWebhookController(ProviderNotifications notifications, IPaymentProvider provider) : ControllerBase
{
    [HttpPost]
    public async Task<NotificationAck> Receive(
        [FromQuery(Name = "data.id")] string? dataId, [FromQuery] string? type,
        [FromHeader(Name = "x-request-id")] string? requestId, [FromHeader(Name = "x-signature")] string? signature,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dataId) || !provider.VerifyNotification(dataId, requestId, signature))
        {
            throw new UnauthorizedException("assinatura da notificação inválida");
        }
        if (!string.Equals(type, "payment", StringComparison.OrdinalIgnoreCase))
        {
            return new NotificationAck("ignored"); // outros tipos (merchant_order, chargebacks) ainda não interessam
        }
        var result = await notifications.ApplyAsync(dataId, ct);
        // desconhecida também responde 200: não é desta loja, e o provedor não precisa insistir
        return new NotificationAck(result switch
        {
            NotificationResult.Applied => "applied",
            NotificationResult.UnknownPayment => "unknown_payment",
            _ => "ignored",
        });
    }
}

public sealed record NotificationAck(string Result);
