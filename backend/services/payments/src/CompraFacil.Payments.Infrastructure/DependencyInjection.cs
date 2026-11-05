using System.Security.Cryptography.X509Certificates;
using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Payments.Application.Payments;
using CompraFacil.Payments.Infrastructure.Persistence;
using CompraFacil.Payments.Infrastructure.Provider;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CompraFacil.Payments.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco, gateway de pagamento e mensageria do payments.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddPostgres<PaymentsDbContext>();
        builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();

        builder.Services.Configure<PaymentOptions>(builder.Configuration.GetSection(PaymentOptions.Section));
        builder.Services.Configure<FeeOptions>(builder.Configuration.GetSection(FeeOptions.Section));
        builder.Services.Configure<PixExpiryOptions>(builder.Configuration.GetSection(PixExpiryOptions.Section));
        builder.AddEfi();
        builder.Services.AddHostedService<PixExpiryJob>();

        // contracts/asyncapi/payments.v1.yaml: junta OrderPlaced + StockReserved e cobra; devolve no cancelamento e
        // no arrependimento
        builder.AddMessaging<PaymentsDbContext>("payments", m => m
            .Produces(PaymentEvents.Topic)
            .Handle<OrderPlacedData, OnOrderPlaced>(PaymentEvents.OrdersTopic, "OrderPlaced")
            .Handle<OrderCancelledData, OnOrderCancelled>(PaymentEvents.OrdersTopic, "OrderCancelled")
            .Handle<OrderReturnedData, OnOrderReturned>(PaymentEvents.OrdersTopic, "OrderReturned")
            .Handle<StockReservedData, OnStockReserved>(PaymentEvents.ReservationsTopic, "StockReserved"));
        return builder;
    }

    /// <summary>
    /// A Efí (#379): as duas APIs, cada uma com o seu cliente HTTP. A API Pix apresenta o certificado .p12 da conta
    /// (mTLS) quando ele está configurado; o simulador não pede.
    /// </summary>
    private static void AddEfi(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<EfiOptions>(builder.Configuration.GetSection(EfiOptions.Section));
        builder.Services.AddHttpClient(EfiApi.ChargesClient, (sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<EfiOptions>>().Value;
            http.BaseAddress = o.ChargesBaseUrl;
            http.Timeout = o.Timeout;
        });
        builder.Services.AddHttpClient(EfiApi.PixClient, (sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<EfiOptions>>().Value;
            http.BaseAddress = o.PixBaseUrl;
            http.Timeout = o.Timeout;
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var o = sp.GetRequiredService<IOptions<EfiOptions>>().Value;
            var handler = new HttpClientHandler();
            if (!string.IsNullOrWhiteSpace(o.PixCertificateBase64))
            {
                handler.ClientCertificateOptions = ClientCertificateOption.Manual;
                handler.ClientCertificates.Add(X509CertificateLoader.LoadPkcs12(
                    Convert.FromBase64String(o.PixCertificateBase64), o.PixCertificatePassword ?? ""));
            }
            return handler;
        });
        builder.Services.AddSingleton<EfiApi>();
        builder.Services.AddSingleton<IPaymentProvider, EfiProvider>();
        builder.Services.AddHostedService<EfiPixWebhookRegistration>();
    }
}
