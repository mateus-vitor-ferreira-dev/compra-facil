using CompraFacil.Fulfillment.Application.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

namespace CompraFacil.Fulfillment.Infrastructure.Invoices;

/// <summary>
/// Emite as notas de devolução pedidas (#223). Como o cancelamento, reserva cada uma (return_claimed_at, FOR
/// UPDATE SKIP LOCKED) antes da chamada: duas réplicas não emitem a mesma, e a que caiu no meio volta à fila
/// depois de um minuto (emitir de novo devolve a mesma nota, pela referência).
/// </summary>
public sealed partial class ReturnInvoiceJob(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ReturnInvoiceJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan s_interval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_lease = TimeSpan.FromMinutes(1);
    private const int BatchSize = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (SuppressInstrumentationScope.Begin())
                {
                    await IssueOnceAsync(stoppingToken);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(logger, e);
            }
            try
            {
                await Task.Delay(s_interval, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Reserva um lote e emite no emissor. Devolve quantas foram reservadas.</summary>
    public async Task<int> IssueOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>();
        var now = clock.GetUtcNow();
        var expired = now - s_lease;
        var claimed = await db.Database.SqlQuery<Guid>($"""
            UPDATE invoices SET return_claimed_at = {now}
            WHERE id IN (
                SELECT id FROM invoices
                WHERE return_requested_at IS NOT NULL AND return_access_key IS NULL AND return_error IS NULL
                  AND (return_claimed_at IS NULL OR return_claimed_at < {expired})
                ORDER BY return_requested_at LIMIT {BatchSize} FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(ct);

        var issuer = scope.ServiceProvider.GetRequiredService<ReturnInvoiceIssuer>();
        foreach (var id in claimed)
        {
            var invoice = await db.Invoices.SingleAsync(i => i.Id == id, ct);
            try
            {
                await issuer.IssueAsync(invoice, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (InvoiceIssuerUnavailableException e)
            {
                // fora do ar: fica reservada até o prazo vencer e volta à fila
                LogUnavailable(logger, e, id);
                db.ChangeTracker.Clear();
            }
        }
        return claimed.Count;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "emissor fora ao emitir a nota de devolução do pedido {OrderId}; nova tentativa depois")]
    private static partial void LogUnavailable(ILogger logger, Exception e, Guid orderId);

    [LoggerMessage(Level = LogLevel.Error, Message = "falha na emissão de notas de devolução; nova tentativa no próximo ciclo")]
    private static partial void LogFailed(ILogger logger, Exception e);
}
