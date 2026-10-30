using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Notifications.Application.Emails;
using CompraFacil.Notifications.Infrastructure.Email;
using CompraFacil.Notifications.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CompraFacil.Notifications.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco, SMTP e mensageria do notifications.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddPostgres<NotificationsDbContext>();
        builder.Services.AddScoped<INotificationRepository, NotificationRepository>();

        var smtp = builder.Configuration.GetSection("Smtp");
        builder.Services.Configure<SmtpOptions>(smtp);
        builder.Services.AddSingleton<IEmailSender, MailKitEmailSender>();
        if (smtp.GetValue("DispatcherEnabled", defaultValue: true))
        {
            builder.Services.AddHostedService<EmailDispatcher>();
        }

        builder.Services.Configure<StoreOptions>(builder.Configuration.GetSection(StoreOptions.Section));

        // contracts/asyncapi/notifications.v1.yaml: consome o resultado do pedido, a nota fiscal e a confirmação de e-mail
        builder.AddMessaging<NotificationsDbContext>("notifications", m => m
            .Handle<OrderConfirmedData, OnOrderConfirmed>(NotificationEvents.OrdersTopic, NotificationEvents.OrderConfirmed)
            .Handle<OrderCancelledData, OnOrderCancelled>(NotificationEvents.OrdersTopic, NotificationEvents.OrderCancelled)
            .Handle<OrderReturnedData, OnOrderReturned>(NotificationEvents.OrdersTopic, NotificationEvents.OrderReturned)
            .Handle<OrderRefundedData, OnOrderRefunded>(NotificationEvents.OrdersTopic, NotificationEvents.OrderRefunded)
            .Handle<OrderShippedData, OnOrderShipped>(NotificationEvents.OrdersTopic, NotificationEvents.OrderShipped)
            .Handle<OrderDeliveredData, OnOrderDelivered>(NotificationEvents.OrdersTopic, NotificationEvents.OrderDelivered)
            .Handle<SupplierReturnOpenedData, OnSupplierReturnOpened>(NotificationEvents.SupplierOrdersTopic, NotificationEvents.SupplierReturnOpened)
            .Handle<InvoiceIssuedData, OnInvoiceIssued>(NotificationEvents.InvoicesTopic, NotificationEvents.InvoiceIssued)
            .Handle<EmailConfirmationRequestedData, OnEmailConfirmationRequested>(
                NotificationEvents.AccountsTopic, NotificationEvents.EmailConfirmationRequested));
        return builder;
    }
}
