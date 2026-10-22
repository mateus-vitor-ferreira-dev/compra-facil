using System.Diagnostics.Metrics;
using CompraFacil.Notifications.Domain;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using OpenTelemetry;

namespace CompraFacil.Notifications.Infrastructure.Email;

/// <summary>SMTP (seção Smtp). Em dev, o Mailpit do compose (porta 1025, sem TLS nem senha).</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string FromAddress { get; set; } = "nao-responda@comprafacil.dev";
    public string FromName { get; set; } = "CompraFácil";
    public bool UseTls { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Espera do despachante quando a fila está vazia.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 20;
    public bool DispatcherEnabled { get; set; } = true;
}

/// <summary>Envio de um e-mail. Lança exceção se o servidor não aceitou.</summary>
public interface IEmailSender
{
    Task SendAsync(Notification notification, CancellationToken ct);
}

internal sealed class MailKitEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(Notification notification, CancellationToken ct)
    {
        var o = options.Value;
        var message = new MimeMessage
        {
            // Message-ID estável por evento: um servidor que deduplica reconhece a repetição
            MessageId = $"{notification.Id}@comprafacil.dev",
            Subject = notification.Subject,
            Body = new BodyBuilder { HtmlBody = notification.HtmlBody, TextBody = notification.TextBody }.ToMessageBody(),
        };
        message.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        message.To.Add(MailboxAddress.Parse(notification.To));

        using var smtp = new SmtpClient { Timeout = (int)o.Timeout.TotalMilliseconds };
        await smtp.ConnectAsync(o.Host, o.Port, o.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
        if (!string.IsNullOrEmpty(o.User))
        {
            await smtp.AuthenticateAsync(o.User, o.Password ?? "", ct);
        }
        await smtp.SendAsync(message, ct);
        await smtp.DisconnectAsync(quit: true, ct);
    }
}

/// <summary>
/// Envia as notificações pendentes. Cada uma é reservada (Pending → Sending, gravado) antes do envio,
/// com FOR UPDATE SKIP LOCKED: duas réplicas não pegam a mesma. Erro de SMTP devolve à fila (até
/// <see cref="Notification.MaxAttempts"/>); processo que cai depois de reservar deixa a notificação em
/// Sending — não reenvia: o contrato prefere perder a duplicar.
/// </summary>
public sealed partial class EmailDispatcher(
    IServiceScopeFactory scopes,
    IEmailSender sender,
    IOptions<SmtpOptions> options,
    TimeProvider clock,
    ILogger<EmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var sent = 0;
            try
            {
                // laço de manutenção: sem traces das consultas de fila vazia
                using (SuppressInstrumentationScope.Begin())
                {
                    sent = await DispatchOnceAsync(stoppingToken);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogDispatchFailed(logger, e);
            }
            if (sent < options.Value.BatchSize)
            {
                try
                {
                    await Task.Delay(options.Value.PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Reserva um lote e envia. Devolve quantas foram reservadas.</summary>
    private static readonly Meter s_meter = new("CompraFacil.Notifications");

    /// <summary>E-mails por tipo e resultado (Sent; Pending = vai tentar de novo; Failed = desistiu).</summary>
    private static readonly Counter<long> s_emails = s_meter.CreateCounter<long>(
        "compra_facil.notifications.emails", unit: "{email}", description: "E-mails por tipo e resultado.");

    public async Task<int> DispatchOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        var batch = options.Value.BatchSize;
        // SKIP LOCKED: réplicas pegam lotes diferentes. O status = 'Pending' de fora é reavaliado pelo
        // Postgres se outra transação mudou a linha antes: ninguém reserva o que já foi reservado.
        var claimed = await db.Database.SqlQuery<Guid>($"""
            UPDATE notifications SET status = 'Sending'
            WHERE status = 'Pending' AND id IN (
                SELECT id FROM notifications WHERE status = 'Pending'
                ORDER BY created_at LIMIT {batch} FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(ct);

        foreach (var id in claimed)
        {
            var notification = await db.Notifications.SingleAsync(n => n.Id == id, ct);
            try
            {
                await sender.SendAsync(notification, ct);
                notification.MarkSent(clock.GetUtcNow());
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                notification.MarkAttemptFailed(e.Message);
                LogSendFailed(logger, e, notification.Id, notification.Attempts, notification.Status);
            }
            await db.SaveChangesAsync(ct);
            s_emails.Add(1, new KeyValuePair<string, object?>("kind", notification.Kind), new KeyValuePair<string, object?>("result", notification.Status.ToString()));
        }
        return claimed.Count;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "falha ao enviar o e-mail {NotificationId} (tentativa {Attempts}, agora {Status})")]
    private static partial void LogSendFailed(ILogger logger, Exception e, Guid notificationId, int attempts, NotificationStatus status);

    [LoggerMessage(Level = LogLevel.Error, Message = "falha no despacho de e-mails; nova tentativa no próximo ciclo")]
    private static partial void LogDispatchFailed(ILogger logger, Exception e);
}
