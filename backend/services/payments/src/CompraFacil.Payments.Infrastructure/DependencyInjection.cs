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
        builder.Services.Configure<MercadoPagoOptions>(builder.Configuration.GetSection(MercadoPagoOptions.Section));
        builder.Services.AddHttpClient<IPaymentProvider, MercadoPagoProvider>((sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<MercadoPagoOptions>>().Value;
            http.BaseAddress = o.BaseUrl;
            http.Timeout = o.Timeout;
        });

        // contracts/asyncapi/payments.v1.yaml: junta OrderPlaced + StockReserved e cobra; captura quando o
        // fornecedor aceita; devolve no cancelamento
        builder.AddMessaging<PaymentsDbContext>("payments", m => m
            .Produces(PaymentEvents.Topic)
            .Handle<OrderPlacedData, OnOrderPlaced>(PaymentEvents.OrdersTopic, "OrderPlaced")
            .Handle<OrderCancelledData, OnOrderCancelled>(PaymentEvents.OrdersTopic, "OrderCancelled")
            .Handle<OrderReturnedData, OnOrderReturned>(PaymentEvents.OrdersTopic, "OrderReturned")
            .Handle<StockReservedData, OnStockReserved>(PaymentEvents.ReservationsTopic, "StockReserved")
            .Handle<SupplierOrderPlacedData, OnSupplierOrderPlaced>(PaymentEvents.FulfillmentTopic, "SupplierOrderPlaced"));
        return builder;
    }
}
