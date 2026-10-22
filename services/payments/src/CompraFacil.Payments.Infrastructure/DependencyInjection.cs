using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Payments.Application.Payments;
using CompraFacil.Payments.Infrastructure.Gateway;
using CompraFacil.Payments.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CompraFacil.Payments.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco, gateway de pagamento e mensageria do payments.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddPostgres<PaymentsDbContext>();
        builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();

        builder.Services.Configure<FakeGatewayOptions>(builder.Configuration.GetSection("Payments:Fake"));
        builder.Services.AddSingleton<FakePaymentGateway>(); // singleton: a idempotência fica em memória
        builder.Services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<FakePaymentGateway>());

        // contracts/asyncapi/payments.v1.yaml: junta OrderPlaced + StockReserved, publica o resultado
        builder.AddMessaging<PaymentsDbContext>("payments", m => m
            .Produces(PaymentEvents.Topic)
            .Handle<OrderPlacedData, OnOrderPlaced>(PaymentEvents.OrdersTopic, "OrderPlaced")
            .Handle<OrderCancelledData, OnOrderCancelled>(PaymentEvents.OrdersTopic, "OrderCancelled")
            .Handle<StockReservedData, OnStockReserved>(PaymentEvents.ReservationsTopic, "StockReserved"));
        return builder;
    }
}
