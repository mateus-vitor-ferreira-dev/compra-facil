using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Infrastructure.Persistence;
using CompraFacil.Fulfillment.Infrastructure.Supplier;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CompraFacil.Fulfillment.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco, fornecedor, despachante e mensageria do fulfillment.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddPostgres<FulfillmentDbContext>();
        builder.Services.AddScoped<ISupplierOrderRepository, SupplierOrderRepository>();

        var supplier = builder.Configuration.GetSection("Supplier");
        builder.Services.Configure<SupplierOptions>(supplier);
        var since = supplier.GetValue<DateTimeOffset?>("AcceptOrdersSince");
        if (since is null && builder.Environment.IsProduction())
        {
            // sem o corte, um fulfillment novo mandaria ao fornecedor (com dinheiro de verdade) os pedidos
            // antigos que ainda estão no Kafka
            throw new InvalidOperationException(
                "Supplier:AcceptOrdersSince é obrigatório em produção (a data a partir da qual os pedidos vão ao fornecedor)");
        }
        builder.Services.AddSingleton(new OrderCutoff(since));
        builder.Services.AddSingleton<CjTokenCache>();
        builder.Services.AddHttpClient<ISupplierGateway, CjSupplierGateway>((sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<SupplierOptions>>().Value;
            http.BaseAddress = o.BaseUrl;
            http.Timeout = o.Timeout;
        });
        if (supplier.GetValue("DispatcherEnabled", defaultValue: true))
        {
            builder.Services.AddHostedService<SupplierDispatcher>();
        }

        // contracts/asyncapi/fulfillment.v1.yaml: o pedido confirmado vai ao fornecedor; o que ele conta vira evento
        builder.AddMessaging<FulfillmentDbContext>("fulfillment", m => m
            .Produces(FulfillmentEvents.Topic)
            .Handle<OrderConfirmedData, OnOrderConfirmed>(FulfillmentEvents.OrdersTopic, FulfillmentEvents.OrderConfirmed));
        return builder;
    }
}
