using CompraFacil.Fulfillment.Application.SupplierOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

namespace CompraFacil.Fulfillment.Infrastructure.Supplier;

/// <summary>
/// Abre no fornecedor as devoluções pedidas (#223). Como o despachante, reserva cada uma (claimed_at, FOR UPDATE
/// SKIP LOCKED) antes da chamada: duas réplicas não abrem a mesma, e a que falhou ou caiu no meio volta à fila
/// depois de um minuto (abrir de novo devolve a mesma disputa).
/// </summary>
public sealed partial class SupplierReturnJob(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SupplierReturnJob> logger)
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
                    await OpenOnceAsync(stoppingToken);
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

    /// <summary>Reserva um lote e abre no fornecedor. Devolve quantas foram reservadas.</summary>
    public async Task<int> OpenOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>();
        var now = clock.GetUtcNow();
        var expired = now - s_lease;
        var claimed = await db.Database.SqlQuery<Guid>($"""
            UPDATE supplier_returns SET claimed_at = {now}
            WHERE id IN (
                SELECT id FROM supplier_returns
                WHERE status = 'Pending' AND opened_at IS NULL AND (claimed_at IS NULL OR claimed_at < {expired})
                ORDER BY requested_at LIMIT {BatchSize} FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(ct);

        var opener = scope.ServiceProvider.GetRequiredService<SupplierReturnOpener>();
        foreach (var id in claimed)
        {
            var supplierReturn = await db.SupplierReturns.SingleAsync(r => r.Id == id, ct);
            await opener.OpenAsync(supplierReturn, ct);
            await db.SaveChangesAsync(ct);
        }
        return claimed.Count;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "falha na abertura de devoluções; nova tentativa no próximo ciclo")]
    private static partial void LogFailed(ILogger logger, Exception e);
}
