using Embrulhado.BuildingBlocks.Data;
using Embrulhado.BuildingBlocks.Messaging;
using Embrulhado.Notifications.Application.Emails;
using Embrulhado.Notifications.Infrastructure.Email;
using Embrulhado.Notifications.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Embrulhado.Notifications.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco, SMTP e mensageria do notifications.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddPostgres<NotificationsDbContext>();
        // a retenção da demonstração (#280): só com Demo:AnonymizeAfter
        builder.AddDemoRetention<NotificationsDemoAnonymizer>();
        builder.Services.AddScoped<INotificationRepository, NotificationRepository>();

        var smtp = builder.Configuration.GetSection("Smtp");
        builder.Services.Configure<SmtpOptions>(smtp);
        builder.Services.AddSingleton<IEmailSender, MailKitEmailSender>();
        if (smtp.GetValue("DispatcherEnabled", defaultValue: true))
        {
            builder.Services.AddHostedService<EmailDispatcher>();
        }

        builder.Services.Configure<StoreOptions>(builder.Configuration.GetSection(StoreOptions.Section));

        // contracts/asyncapi/notifications.v1.yaml: consome o resultado do pedido, a nota fiscal, a confirmação de e-mail e a redefinição de senha
        builder.AddMessaging<NotificationsDbContext>("notifications", m => m
            .Handle<OrderConfirmedData, OnOrderConfirmed>(NotificationEvents.OrdersTopic, NotificationEvents.OrderConfirmed)
            .Handle<OrderCancelledData, OnOrderCancelled>(NotificationEvents.OrdersTopic, NotificationEvents.OrderCancelled)
            .Handle<OrderReturnedData, OnOrderReturned>(NotificationEvents.OrdersTopic, NotificationEvents.OrderReturned)
            .Handle<OrderRefundedData, OnOrderRefunded>(NotificationEvents.OrdersTopic, NotificationEvents.OrderRefunded)
            .Handle<OrderShippedData, OnOrderShipped>(NotificationEvents.OrdersTopic, NotificationEvents.OrderShipped)
            .Handle<OrderDeliveredData, OnOrderDelivered>(NotificationEvents.OrdersTopic, NotificationEvents.OrderDelivered)
            .Handle<SupplierReturnOpenedData, OnSupplierReturnOpened>(NotificationEvents.SupplierOrdersTopic, NotificationEvents.SupplierReturnOpened)
            .Handle<SupplierReturnResolvedData, OnSupplierReturnResolved>(
                NotificationEvents.SupplierOrdersTopic, NotificationEvents.SupplierReturnResolved)
            .Handle<CustomerReturnInstructedData, OnCustomerReturnInstructed>(
                NotificationEvents.SupplierOrdersTopic, NotificationEvents.CustomerReturnInstructed)
            .Handle<InvoiceIssuedData, OnInvoiceIssued>(NotificationEvents.InvoicesTopic, NotificationEvents.InvoiceIssued)
            .Handle<GuestAccessRequestedData, OnGuestAccessRequested>(
                NotificationEvents.AccountsTopic, NotificationEvents.GuestAccessRequested)
            .Handle<EmailConfirmationRequestedData, OnEmailConfirmationRequested>(
                NotificationEvents.AccountsTopic, NotificationEvents.EmailConfirmationRequested)
            .Handle<PasswordResetRequestedData, OnPasswordResetRequested>(
                NotificationEvents.AccountsTopic, NotificationEvents.PasswordResetRequested));
        return builder;
    }
}
