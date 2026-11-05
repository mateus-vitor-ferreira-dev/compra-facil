using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Payments.Application.Payments;
using CompraFacil.Payments.Infrastructure.Provider;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CompraFacil.Payments.Api.Controllers;

/// <summary>
/// Os avisos da Efí (contracts/openapi/payments.v1.yaml). Nenhum é a verdade: o do PIX diz o txid, o do cartão
/// só um token, e a cobrança é consultada na Efí. Cobrança desconhecida também responde 200, para a Efí não
/// insistir.
/// </summary>
[ApiController]
[Route("v1/webhooks/efi")]
[Produces("application/json")]
public sealed partial class EfiWebhooksController(
    ProviderNotifications notifications, IOptions<EfiOptions> options, ILogger<EfiWebhooksController> logger) : ControllerBase
{
    /// <summary>
    /// receivePixNotification: o PIX pago (ou a devolução concluída). A URL cadastrada tem o segredo (?hmac=), e a
    /// Efí acrescenta "/pix" ao fim dela, que cai dentro do ?ignorar=.
    /// </summary>
    [HttpPost("pix")]
    public async Task<NotificationAck> Pix([FromQuery] string? hmac, CancellationToken ct)
    {
        var secret = options.Value.PixWebhookSecret;
        if (string.IsNullOrEmpty(secret) || hmac is null
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hmac), Encoding.UTF8.GetBytes(secret)))
        {
            throw new UnauthorizedException("segredo do webhook do PIX inválido");
        }
        JsonDocument body;
        try
        {
            body = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return Ack(NotificationResult.Ignored); // o aviso de teste do cadastro, ou outro formato: nada a aplicar
        }
        using var _ = body;
        var result = NotificationResult.Ignored;
        if (body.RootElement.ValueKind == JsonValueKind.Object && body.RootElement.TryGetProperty("pix", out var items)
            && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("txid", out var txid) || txid.GetString() is not { Length: > 0 } reference)
                {
                    continue; // o PIX sem cobrança (QR estático, PIX enviado): não é desta loja
                }
                WarnRefusedRefunds(item, reference);
                var one = await notifications.ApplyAsync(reference, ct);
                result = Merge(result, one);
            }
        }
        return Ack(result);
    }

    /// <summary>receiveChargeNotification: só o token (form notification=), consultado na Efí.</summary>
    [HttpPost("charges")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<ActionResult<NotificationAck>> Charges([FromForm] string? notification, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(notification) || notification.Length > 64)
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Dados inválidos", detail: "notification é obrigatório");
        }
        try
        {
            return Ack(await notifications.ApplyTokenAsync(notification, ct));
        }
        catch (PaymentProviderRejectedException)
        {
            return Ack(NotificationResult.UnknownPayment); // token que a Efí não conhece: não é desta loja
        }
    }

    private void WarnRefusedRefunds(JsonElement item, string txid)
    {
        if (!item.TryGetProperty("devolucoes", out var refunds) || refunds.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        foreach (var refund in refunds.EnumerateArray())
        {
            if (refund.TryGetProperty("status", out var status) && status.GetString() == "NAO_REALIZADO")
            {
                // o estorno já foi contado: o dinheiro não voltou ao cliente, e a equipe devolve pelo painel da Efí
                LogRefundRefused(logger, txid, refund.TryGetProperty("motivo", out var m) ? m.GetString() : null);
            }
        }
    }

    private static NotificationResult Merge(NotificationResult acc, NotificationResult one) =>
        acc == NotificationResult.Applied || one == NotificationResult.Applied ? NotificationResult.Applied
        : one == NotificationResult.UnknownPayment ? NotificationResult.UnknownPayment : acc;

    private static NotificationAck Ack(NotificationResult result) => new(result switch
    {
        NotificationResult.Applied => "applied",
        NotificationResult.UnknownPayment => "unknown_payment",
        _ => "ignored",
    });

    [LoggerMessage(Level = LogLevel.Error, Message = "a Efí não realizou a devolução do PIX {Txid} ({Reason}): devolver pelo painel da Efí")]
    private static partial void LogRefundRefused(ILogger logger, string txid, string? reason);
}

public sealed record NotificationAck(string Result);
