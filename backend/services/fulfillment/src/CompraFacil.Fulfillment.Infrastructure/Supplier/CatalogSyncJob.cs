using CompraFacil.Fulfillment.Application.SupplierCatalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompraFacil.Fulfillment.Infrastructure.Supplier;

/// <summary>
/// Lê o feed do fornecedor ao subir e a cada <see cref="SupplierOptions.CatalogSyncInterval"/>. Em várias
/// réplicas, a que não pega o lock pula a rodada: a outra já está lendo.
/// </summary>
public sealed partial class CatalogSyncJob(
    IServiceScopeFactory scopes, IOptions<SupplierOptions> options, TimeProvider clock, ILogger<CatalogSyncJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<CatalogSync>().RunAsync(stoppingToken);
            }
            catch (CatalogSyncBusyException)
            {
                LogBusy(logger);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(logger, e);
            }
            try
            {
                await Task.Delay(options.Value.CatalogSyncInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "outra réplica está lendo o feed do fornecedor; esta pula a rodada")]
    private static partial void LogBusy(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "falha ao ler o feed do fornecedor; nada mudou, nova tentativa no próximo ciclo")]
    private static partial void LogFailed(ILogger logger, Exception e);
}
