using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompraFacil.BuildingBlocks.Data;

/// <summary>
/// Seção Demo: a retenção dos dados pessoais da loja de demonstração (#280). Sem <see cref="AnonymizeAfter"/>, nada
/// roda: na loja de verdade, a nota fiscal obriga a guardar os dados por anos, e ninguém apaga nada por prazo.
/// </summary>
public sealed class DemoRetentionOptions
{
    public const string Section = "Demo";

    /// <summary>A idade a partir da qual os dados pessoais são mascarados; nulo, desligado.</summary>
    public TimeSpan? AnonymizeAfter { get; set; }

    /// <summary>De quanto em quanto tempo o job roda.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>Mascara os dados pessoais do serviço (nome, CPF, telefone, endereço, corpo de e-mail) criados antes do corte.</summary>
public interface IPersonalDataAnonymizer
{
    /// <returns>Quantos registros mudaram.</returns>
    Task<int> AnonymizeAsync(DateTimeOffset createdBefore, CancellationToken ct);
}

/// <summary>
/// O job da retenção da demonstração: a cada <see cref="DemoRetentionOptions.Interval"/>, mascara o que passou de
/// <see cref="DemoRetentionOptions.AnonymizeAfter"/>. Os registros continuam (pedidos, margens, histórico), sem os
/// dados de quem comprou. Idempotente: o que já foi mascarado não muda de novo.
/// </summary>
public sealed partial class DemoRetentionJob(
    IServiceScopeFactory scopes, IOptions<DemoRetentionOptions> options, TimeProvider clock, ILogger<DemoRetentionJob> logger) : BackgroundService
{
    /// <summary>Uma rodada; o laço chama, e os testes também.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var after = options.Value.AnonymizeAfter ?? throw new InvalidOperationException("Demo:AnonymizeAfter não configurado");
        await using var scope = scopes.CreateAsyncScope();
        var changed = await scope.ServiceProvider.GetRequiredService<IPersonalDataAnonymizer>().AnonymizeAsync(clock.GetUtcNow() - after, ct);
        if (changed > 0)
        {
            LogAnonymized(logger, changed, after);
        }
        return changed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(logger, e);
            }
            try
            {
                await Task.Delay(options.Value.Interval, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "demonstração: dados pessoais de {Count} registros com mais de {After} mascarados")]
    private static partial void LogAnonymized(ILogger logger, int count, TimeSpan after);

    [LoggerMessage(Level = LogLevel.Error, Message = "demonstração: a anonimização falhou; nova tentativa na próxima rodada")]
    private static partial void LogFailed(ILogger logger, Exception e);
}

public static class DemoRetentionExtensions
{
    /// <summary>Liga a retenção da demonstração quando <c>Demo:AnonymizeAfter</c> está configurado; sem ele, não registra nada.</summary>
    public static IHostApplicationBuilder AddDemoRetention<TAnonymizer>(this IHostApplicationBuilder builder)
        where TAnonymizer : class, IPersonalDataAnonymizer
    {
        ArgumentNullException.ThrowIfNull(builder);
        var section = builder.Configuration.GetSection(DemoRetentionOptions.Section);
        builder.Services.Configure<DemoRetentionOptions>(section);
        builder.Services.AddScoped<IPersonalDataAnonymizer, TAnonymizer>();
        if (section.Get<DemoRetentionOptions>()?.AnonymizeAfter is not null)
        {
            builder.Services.AddSingleton<DemoRetentionJob>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<DemoRetentionJob>());
        }
        return builder;
    }
}
