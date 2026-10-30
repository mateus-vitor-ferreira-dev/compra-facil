using CompraFacil.Fulfillment.Application.SupplierOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace CompraFacil.Fulfillment.Infrastructure.Supplier;

/// <summary>
/// Leva os pedidos da fila ao fornecedor. Cada um é reservado (Pending → Placing, gravado) com FOR
/// UPDATE SKIP LOCKED antes da chamada: duas réplicas não pegam o mesmo. Se o processo cair no meio, o
/// pedido fica em Placing e volta à fila depois de <see cref="SupplierOptions.ClaimLease"/>: reenviar é
/// seguro, porque o fornecedor deduplica pelo número do pedido.
/// </summary>
public sealed partial class SupplierDispatcher(
    IServiceScopeFactory scopes,
    IOptions<SupplierOptions> options,
    TimeProvider clock,
    ILogger<SupplierDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var placed = 0;
            try
            {
                // laço de manutenção: sem traces das consultas de fila vazia
                using (SuppressInstrumentationScope.Begin())
                {
                    placed = await DispatchOnceAsync(stoppingToken);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogDispatchFailed(logger, e);
            }
            if (placed < options.Value.BatchSize)
            {
                try
                {
                    await Task.Delay(options.Value.PollInterval, clock, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Reserva um lote e envia ao fornecedor. Devolve quantos foram reservados.</summary>
    public async Task<int> DispatchOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>();
        var now = clock.GetUtcNow();
        var expired = now - options.Value.ClaimLease;
        var batch = options.Value.BatchSize;
        // SKIP LOCKED: réplicas pegam lotes diferentes; o pedido em espera (next_attempt_at) fica para depois
        var claimed = await db.Database.SqlQuery<Guid>($"""
            UPDATE supplier_orders SET status = 'Placing', claimed_at = {now}
            WHERE id IN (
                SELECT id FROM supplier_orders
                WHERE (status = 'Pending' AND (next_attempt_at IS NULL OR next_attempt_at <= {now}))
                   OR (status = 'Placing' AND claimed_at < {expired})
                ORDER BY created_at LIMIT {batch} FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(ct);

        var placer = scope.ServiceProvider.GetRequiredService<SupplierOrderPlacer>();
        foreach (var id in claimed)
        {
            var order = await db.SupplierOrders.SingleAsync(o => o.Id == id, ct);
            var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == id, ct);
            await placer.PlaceAsync(order, invoice, ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // um webhook mudou o pedido no meio: ele já está à frente; esta tentativa não grava nada
                LogConcurrent(logger, id);
                db.ChangeTracker.Clear();
            }
        }
        return claimed.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "o pedido {OrderId} mudou no meio do envio (webhook); fica como o webhook deixou")]
    private static partial void LogConcurrent(ILogger logger, Guid orderId);

    [LoggerMessage(Level = LogLevel.Error, Message = "falha no despacho de pedidos ao fornecedor; nova tentativa no próximo ciclo")]
    private static partial void LogDispatchFailed(ILogger logger, Exception e);
}
