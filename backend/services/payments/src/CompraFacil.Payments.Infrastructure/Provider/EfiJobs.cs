using CompraFacil.Payments.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompraFacil.Payments.Infrastructure.Provider;

/// <summary>
/// Cadastra o webhook do PIX na Efí ao subir (PUT /v2/webhook/:chave), com o segredo na URL e "ignorar=" no fim:
/// a Efí acrescenta "/pix" ao caminho, e assim ele cai dentro do parâmetro. A Efí chama a URL no cadastro, então
/// espera a aplicação estar no ar e tenta de novo, com espera crescente, se ela ainda não responde.
/// </summary>
internal sealed partial class EfiPixWebhookRegistration(
    EfiApi api, IOptions<EfiOptions> options, IHostApplicationLifetime lifetime, ILogger<EfiPixWebhookRegistration> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (o.PixWebhookUrl is not { } url || string.IsNullOrWhiteSpace(o.PixWebhookSecret))
        {
            LogSkipped(logger);
            return;
        }
        var started = new TaskCompletionSource();
        using var _ = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken);

        var target = $"{url.AbsoluteUri}?hmac={Uri.EscapeDataString(o.PixWebhookSecret)}&ignorar=";
        var headers = o.PixWebhookSkipMtls ? new Dictionary<string, string> { ["x-skip-mtls-checking"] = "true" } : null;
        var wait = TimeSpan.FromSeconds(5);
        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await api.SendAsync<object>(EfiApiKind.Pix, HttpMethod.Put, $"v2/webhook/{Uri.EscapeDataString(o.PixKey)}",
                    new { webhookUrl = target }, stoppingToken, headers);
                LogRegistered(logger, url.AbsoluteUri);
                return;
            }
            catch (Exception e) when (e is PaymentProviderUnavailableException or EfiApiException)
            {
                LogFailed(logger, e, attempt, wait);
                await Task.Delay(wait, stoppingToken);
                wait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, TimeSpan.FromMinutes(10).Ticks));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payments:Efi:PixWebhookUrl ou PixWebhookSecret vazio: o webhook do PIX não é cadastrado, e o pagamento só chega pela conferência do PIX vencido")]
    private static partial void LogSkipped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "webhook do PIX cadastrado na Efí: {Url}")]
    private static partial void LogRegistered(ILogger logger, string url);

    [LoggerMessage(Level = LogLevel.Error, Message = "o cadastro do webhook do PIX falhou (tentativa {Attempt}); de novo em {Wait}")]
    private static partial void LogFailed(ILogger logger, Exception e, int attempt, TimeSpan wait);
}

/// <summary>Seção Payments:PixExpiry: de quanto em quanto tempo o payments confere os PIX vencidos.</summary>
public sealed class PixExpiryOptions
{
    public const string Section = "Payments:PixExpiry";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    public int BatchSize { get; set; } = 50;
}

/// <summary>Confere os PIX vencidos (<see cref="PixExpiry"/>): a Efí não avisa quando a validade passa.</summary>
internal sealed partial class PixExpiryJob(
    IServiceScopeFactory scopes, IOptions<PixExpiryOptions> options, TimeProvider clock, ILogger<PixExpiryJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval, clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var expired = await scope.ServiceProvider.GetRequiredService<PixExpiry>().ExpireDueAsync(options.Value.BatchSize, stoppingToken);
                if (expired > 0)
                {
                    LogExpired(logger, expired);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(logger, e); // a próxima volta tenta de novo
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} PIX vencidos conferidos na Efí")]
    private static partial void LogExpired(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "a conferência dos PIX vencidos falhou")]
    private static partial void LogFailed(ILogger logger, Exception e);
}
