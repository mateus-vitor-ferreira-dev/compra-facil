namespace CompraFacil.Notifications.Domain;

public enum NotificationStatus
{
    /// <summary>Na fila para envio.</summary>
    Pending,

    /// <summary>Reservada por um despachante; o envio está acontecendo (ou o processo caiu no meio).</summary>
    Sending,

    Sent,

    /// <summary>Desistiu depois de <see cref="Notification.MaxAttempts"/> erros de SMTP.</summary>
    Failed,
}

/// <summary>
/// Um e-mail a enviar, um por evento (Id = event_id). Garantia do contrato: sai no máximo uma vez.
/// Por isso a notificação é reservada (<see cref="NotificationStatus.Sending"/>, gravado) antes do envio:
/// se o processo cair no meio, ela não volta para a fila — fica parada em Sending, à vista de quem opera,
/// em vez de arriscar um e-mail duplicado.
/// </summary>
public sealed class Notification
{
    public const int MaxAttempts = 5;

    private Notification()
    {
        // EF Core
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string Kind { get; private set; } = "";
    public string To { get; private set; } = "";
    public string Subject { get; private set; } = "";
    public string HtmlBody { get; private set; } = "";
    public string TextBody { get; private set; } = "";
    public NotificationStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    public static Notification Create(
        Guid eventId, Guid orderId, string kind, string to, string subject, string htmlBody, string textBody, DateTimeOffset now) => new()
        {
            Id = eventId,
            OrderId = orderId,
            Kind = kind,
            To = to,
            Subject = subject,
            HtmlBody = htmlBody,
            TextBody = textBody,
            Status = NotificationStatus.Pending,
            CreatedAt = now,
        };

    public void MarkSent(DateTimeOffset now)
    {
        Status = NotificationStatus.Sent;
        SentAt = now;
        LastError = null;
    }

    /// <summary>Erro de SMTP (o e-mail não saiu): volta para a fila, ou desiste depois do limite.</summary>
    public void MarkAttemptFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 500 ? error[..500] : error;
        Status = Attempts >= MaxAttempts ? NotificationStatus.Failed : NotificationStatus.Pending;
    }
}
