using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CompraFacil.Orders.Application.Orders;
using CompraFacil.Orders.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CompraFacil.Orders.Api.Controllers;

/// <summary>
/// streamOrderEvents: o pedido ao vivo em Server-Sent Events (contracts/openapi/orders.v1.yaml). Manda o
/// estado atual e cada mudança da SAGA; fecha no estado final. Quem pode ver é quem pode ler o pedido
/// (o dono ou um operador). O EventSource do navegador reconecta sozinho com Last-Event-ID.
/// </summary>
[ApiController]
public sealed class OrderEventsController(OrderLive live, IOptions<JsonOptions> json, TimeProvider clock) : ControllerBase
{
    // espera sugerida ao EventSource antes de reconectar
    private const int RetryMilliseconds = 3000;

    [HttpGet("v1/orders/{order_id:guid}/events")]
    [Authorize]
    public async Task<IActionResult> Stream(
        [FromRoute(Name = "order_id")] Guid orderId,
        [FromHeader(Name = "Last-Event-ID")] string? lastEventId,
        CancellationToken ct)
    {
        var isOperator = User.IsInRole("admin");
        if (!isOperator && !User.IsInRole("customer"))
        {
            return Forbid();
        }
        var customerId = Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : (Guid?)null;

        // 404 aqui, antes de abrir o stream: o erro ainda sai em ProblemDetails
        using var watch = await live.OpenAsync(orderId, new OrderReader(customerId, isOperator), ct);
        var options = live.Options;
        if (OrderWatch.IsFinal(watch.Current) && lastEventId == OrderWatch.EventId(watch.Current))
        {
            return NoContent(); // o cliente já tem o estado final: 204 faz o EventSource parar de reconectar
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // proxies que bufferizam por padrão (nginx)
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            await WriteAsync($"retry: {RetryMilliseconds}\n\n", ct);
            if (lastEventId != OrderWatch.EventId(watch.Current))
            {
                await SendAsync(watch.Current, ct);
            }
            var deadline = clock.GetUtcNow() + options.MaxDuration;
            var lastWrite = clock.GetUtcNow();
            while (!OrderWatch.IsFinal(watch.Current) && clock.GetUtcNow() < deadline)
            {
                var untilHeartbeat = options.HeartbeatInterval - (clock.GetUtcNow() - lastWrite);
                var changed = await watch.NextAsync(untilHeartbeat > TimeSpan.Zero ? untilHeartbeat : TimeSpan.Zero, ct);
                if (changed is not null)
                {
                    await SendAsync(changed, ct);
                    lastWrite = clock.GetUtcNow();
                }
                else if (clock.GetUtcNow() - lastWrite >= options.HeartbeatInterval)
                {
                    await WriteAsync(": keep-alive\n\n", ct);
                    lastWrite = clock.GetUtcNow();
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // o cliente fechou a conexão
        }
        return new EmptyResult();
    }

    private Task SendAsync(Order order, CancellationToken ct)
    {
        var data = JsonSerializer.Serialize(OrderResponse.From(order), json.Value.JsonSerializerOptions);
        return WriteAsync(string.Create(CultureInfo.InvariantCulture,
            $"id: {OrderWatch.EventId(order)}\nevent: order\ndata: {data}\n\n"), ct);
    }

    private async Task WriteAsync(string text, CancellationToken ct)
    {
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
        await Response.Body.FlushAsync(ct);
    }
}
