using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Contracts.Catalog.V1;
using CompraFacil.Orders.Application.Catalog;
using CompraFacil.Orders.Application.Orders;
using CompraFacil.Orders.Infrastructure.Catalog;
using CompraFacil.Orders.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CompraFacil.Orders.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco, catálogo (gRPC) e mensageria do orders.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddPostgres<OrdersDbContext>();
        builder.Services.AddScoped<IOrderRepository, OrderRepository>();
        builder.Services.AddScoped<ICatalogSnapshotStore, CatalogSnapshotStore>();

        builder.Services.Configure<CatalogOptions>(builder.Configuration.GetSection("Catalog"));
        builder.Services.AddGrpcClient<CatalogService.CatalogServiceClient>((sp, o) =>
                o.Address = sp.GetRequiredService<IOptions<CatalogOptions>>().Value.GrpcAddress)
            // cancelamento pelo prazo do Polly vira OperationCanceledException (e o Polly, timeout)
            .ConfigureChannel(c => c.ThrowOperationCanceledOnCancellation = true);
        builder.Services.AddCatalogPricing();

        // contracts/asyncapi/orders.v1.yaml: publica orders.order.v1; consome o resultado da SAGA e o
        // catálogo (snapshot)
        builder.AddMessaging<OrdersDbContext>("orders", m => m
            .Produces(OrderEvents.Topic)
            .Handle<StockReservationFailedData, OnStockReservationFailed>(OrderEvents.ReservationsTopic, "StockReservationFailed")
            .Handle<PaymentApprovedData, OnPaymentApproved>(OrderEvents.PaymentsTopic, "PaymentApproved")
            .Handle<PaymentFailedData, OnPaymentFailed>(OrderEvents.PaymentsTopic, "PaymentFailed")
            .Handle<ProductSnapshotData, ProductChanged>(CatalogEvents.ProductsTopic, "ProductCreated")
            .Handle<ProductSnapshotData, ProductChanged>(CatalogEvents.ProductsTopic, "ProductUpdated")
            .Handle<ProductDeletedData, ProductDeleted>(CatalogEvents.ProductsTopic, "ProductDeleted")
            .Handle<CampaignSnapshotData, CampaignChanged>(CatalogEvents.CampaignsTopic, "CampaignCreated")
            .Handle<CampaignSnapshotData, CampaignChanged>(CatalogEvents.CampaignsTopic, "CampaignUpdated")
            .Handle<CampaignDeletedData, CampaignDeleted>(CatalogEvents.CampaignsTopic, "CampaignDeleted"));
        return builder;
    }
}
