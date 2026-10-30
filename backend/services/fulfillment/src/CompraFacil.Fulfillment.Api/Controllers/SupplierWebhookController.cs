using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Infrastructure.Supplier;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CompraFacil.Fulfillment.Api.Controllers;

/// <summary>
/// receiveSupplierWebhook (contracts/openapi/fulfillment.v1.yaml): o fornecedor avisa cada mudança de
/// status. A assinatura (HMAC-SHA256 do corpo cru) é conferida antes de ler o JSON.
/// </summary>
[ApiController]
[Route("v1/webhooks/supplier")]
[Produces("application/json")]
public sealed class SupplierWebhookController(
    SupplierUpdates updates, SupplierStock stock, SupplierReturnUpdates returns, IOptions<SupplierOptions> options) : ControllerBase
{
    private const int MaxBody = 64 * 1024;

    [HttpPost]
    public async Task<ActionResult<SupplierWebhookAck>> Receive(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        if (buffer.Length is 0 or > MaxBody)
        {
            throw Invalid("corpo vazio ou grande demais");
        }
        var body = buffer.ToArray();
        if (!SignatureValid(body, Request.Headers[options.Value.SignatureHeader].ToString(), options.Value.WebhookSecret))
        {
            throw new UnauthorizedException("assinatura do webhook inválida");
        }

        SupplierWebhook? webhook;
        try
        {
            webhook = JsonSerializer.Deserialize<SupplierWebhook>(body, s_json);
        }
        catch (JsonException)
        {
            throw Invalid("JSON fora do formato do webhook");
        }
        if (webhook?.Params is not { } p || string.IsNullOrWhiteSpace(webhook.MessageId))
        {
            throw Invalid("faltam messageId ou params");
        }
        if (string.Equals(webhook.Type, "STOCK", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(p.Vid) || p.StorageNum is not { } available)
            {
                throw Invalid("faltam params.vid ou params.storageNum");
            }
            var at = DateTimeOffset.TryParse(p.UpdateDate, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : (DateTimeOffset?)null;
            return new SupplierWebhookAck(await stock.ApplyWebhookAsync(webhook.MessageId, p.Vid, available, at, ct) ? "applied" : "duplicate");
        }
        if (!Guid.TryParse(p.OrderNumber, out var orderId))
        {
            throw Invalid("falta params.orderNumber (uuid)");
        }
        if (string.Equals(webhook.Type, "DISPUTE", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(p.DisputeStatus))
            {
                throw Invalid("falta params.disputeStatus");
            }
            var applied = await returns.ApplyAsync(webhook.MessageId, orderId, p.DisputeStatus, p.Reason, ct);
            return new SupplierWebhookAck(applied switch
            {
                SupplierReturnUpdateResult.Applied => "applied",
                SupplierReturnUpdateResult.Duplicate => "duplicate",
                SupplierReturnUpdateResult.UnknownOrder => "unknown_order",
                _ => "ignored",
            });
        }
        if (!string.Equals(webhook.Type, "ORDER", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(p.OrderStatus))
        {
            return new SupplierWebhookAck("ignored"); // outros tipos (produto) ainda não interessam
        }

        var result = await updates.ApplyAsync(new SupplierUpdate(
            webhook.MessageId, orderId, p.OrderId, p.OrderStatus, p.TrackNumber, p.TrackingProvider, p.TrackingUrl, p.Reason), ct);
        return new SupplierWebhookAck(result switch
        {
            SupplierUpdateResult.Applied => "applied",
            SupplierUpdateResult.Duplicate => "duplicate",
            SupplierUpdateResult.UnknownOrder => "unknown_order",
            _ => "ignored",
        });
    }

    private static ValidationException Invalid(string message) => new([new ValidationFailure("body", message)]);

    /// <summary>"sha256=&lt;hex&gt;" igual ao HMAC do corpo, em tempo constante.</summary>
    internal static bool SignatureValid(byte[] body, string header, string secret)
    {
        const string prefix = "sha256=";
        if (!header.StartsWith(prefix, StringComparison.Ordinal) || string.IsNullOrEmpty(secret))
        {
            return false;
        }
        byte[] received;
        try
        {
            received = Convert.FromHexString(header[prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    // o webhook vem no formato do fornecedor (camelCase), não no snake_case da loja
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private sealed record SupplierWebhook(string? MessageId, string? Type, string? MessageType, WebhookParams? Params);

    // ORDER: os campos do pedido; STOCK: vid, storageNum e updateDate; DISPUTE: orderNumber, disputeStatus e reason
    private sealed record WebhookParams(
        string? OrderId, string? OrderNumber, string? OrderStatus, string? SubStatus, string? TrackNumber,
        string? TrackingProvider, string? TrackingUrl, string? Reason, string? Vid, int? StorageNum, string? UpdateDate, string? DisputeStatus);
}

public sealed record SupplierWebhookAck(string Result);
