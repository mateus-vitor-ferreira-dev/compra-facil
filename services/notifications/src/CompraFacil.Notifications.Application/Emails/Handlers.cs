using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Notifications.Domain;

namespace CompraFacil.Notifications.Application.Emails;

public interface INotificationRepository
{
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);

    /// <summary>Entra no SaveChanges do consumer (mesma transação do inbox).</summary>
    void Add(Notification notification);
}

/// <summary>
/// Registra o e-mail (Id = event_id) na transação do evento. O envio é do <c>EmailDispatcher</c>:
/// mandar daqui poderia repetir o e-mail se o processo caísse antes do commit.
/// </summary>
public sealed class OnOrderConfirmed(INotificationRepository notifications, TimeProvider clock) : IHandleEvent<OrderConfirmedData>
{
    public async Task HandleAsync(OrderConfirmedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(context);
        if (await notifications.ExistsAsync(context.EventId, ct))
        {
            return;
        }
        var email = await EmailComposer.ConfirmedAsync(data);
        notifications.Add(Notification.Create(context.EventId, data.OrderId, NotificationEvents.OrderConfirmed,
            data.CustomerEmail, email.Subject, email.HtmlBody, email.TextBody, clock.GetUtcNow()));
    }
}

/// <summary>Registra o e-mail de cancelamento (falta de estoque ou pagamento recusado).</summary>
public sealed class OnOrderCancelled(INotificationRepository notifications, TimeProvider clock) : IHandleEvent<OrderCancelledData>
{
    public async Task HandleAsync(OrderCancelledData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(context);
        if (await notifications.ExistsAsync(context.EventId, ct))
        {
            return;
        }
        var email = await EmailComposer.CancelledAsync(data);
        notifications.Add(Notification.Create(context.EventId, data.OrderId, NotificationEvents.OrderCancelled,
            data.CustomerEmail, email.Subject, email.HtmlBody, email.TextBody, clock.GetUtcNow()));
    }
}

/// <summary>Registra o e-mail com o link de confirmação da conta.</summary>
public sealed class OnEmailConfirmationRequested(INotificationRepository notifications, TimeProvider clock) : IHandleEvent<EmailConfirmationRequestedData>
{
    public async Task HandleAsync(EmailConfirmationRequestedData data, EventContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(context);
        if (await notifications.ExistsAsync(context.EventId, ct))
        {
            return;
        }
        var email = await EmailComposer.ConfirmEmailAsync(data, clock.GetUtcNow());
        notifications.Add(Notification.Create(context.EventId, data.AccountId, NotificationEvents.EmailConfirmationRequested,
            data.Email, email.Subject, email.HtmlBody, email.TextBody, clock.GetUtcNow()));
    }
}
