using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Contracts.Catalog.V1;
using CompraFacil.Orders.Application.Catalog;
using CompraFacil.Orders.Application.Orders;
using CompraFacil.Orders.Infrastructure.Catalog;
using CompraFacil.Orders.Infrastructure.Live;
using CompraFacil.Orders.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
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

        // o frete recotado no fulfillment (#222)
        var fulfillment = builder.Configuration.GetSection(Shipping.FulfillmentOptions.Section).Get<Shipping.FulfillmentOptions>() ?? new();
        if (fulfillment.BaseUrl is { } fulfillmentUrl)
        {
            builder.Services.AddHttpClient<Application.Shipping.IShippingQuotes, Shipping.FulfillmentShippingQuotes>(http =>
            {
                http.BaseAddress = fulfillmentUrl;
                http.Timeout = fulfillment.Timeout;
            });
        }
        else
        {
            builder.Services.AddSingleton<Application.Shipping.IShippingQuotes, Shipping.NoShippingQuotes>();
        }

        // contracts/asyncapi/orders.v1.yaml: publica orders.order.v1; consome o resultado da SAGA, o
        // andamento no fornecedor, o estorno e o catálogo (snapshot)
        builder.AddMessaging<OrdersDbContext>("orders", m => m
            .Produces(OrderEvents.Topic)
            .Handle<StockReservedData, OnStockReserved>(OrderEvents.ReservationsTopic, "StockReserved")
            .Handle<StockReservationFailedData, OnStockReservationFailed>(OrderEvents.ReservationsTopic, "StockReservationFailed")
            .Handle<PaymentApprovedData, OnPaymentApproved>(OrderEvents.PaymentsTopic, "PaymentApproved")
            .Handle<PaymentFailedData, OnPaymentFailed>(OrderEvents.PaymentsTopic, "PaymentFailed")
            .Handle<PaymentRefundedData, OnPaymentRefunded>(OrderEvents.PaymentsTopic, "PaymentRefunded")
            .Handle<PaymentPendingData, OnPaymentPending>(OrderEvents.PaymentsTopic, OrderEvents.PaymentPending)
            .Handle<SupplierOrderPlacedData, OnSupplierOrderPlaced>(OrderEvents.FulfillmentTopic, "SupplierOrderPlaced")
            .Handle<SupplierOrderShippedData, OnSupplierOrderShipped>(OrderEvents.FulfillmentTopic, "SupplierOrderShipped")
            .Handle<SupplierOrderDeliveredData, OnSupplierOrderDelivered>(OrderEvents.FulfillmentTopic, "SupplierOrderDelivered")
            .Handle<SupplierOrderFailedData, OnSupplierOrderFailed>(OrderEvents.FulfillmentTopic, "SupplierOrderFailed")
            .Handle<InvoiceIssuedData, OnInvoiceIssued>(OrderEvents.InvoicesTopic, "InvoiceIssued")
            .Handle<InvoiceReturnIssuedData, OnInvoiceReturnIssued>(OrderEvents.InvoicesTopic, "InvoiceReturnIssued")
            .Handle<InvoiceCancelledData, OnInvoiceCancelled>(OrderEvents.InvoicesTopic, "InvoiceCancelled")
            .Handle<ProductSnapshotData, ProductChanged>(CatalogEvents.ProductsTopic, "ProductCreated")
            .Handle<ProductSnapshotData, ProductChanged>(CatalogEvents.ProductsTopic, "ProductUpdated")
            .Handle<ProductDeletedData, ProductDeleted>(CatalogEvents.ProductsTopic, "ProductDeleted")
            .Handle<CampaignSnapshotData, CampaignChanged>(CatalogEvents.CampaignsTopic, "CampaignCreated")
            .Handle<CampaignSnapshotData, CampaignChanged>(CatalogEvents.CampaignsTopic, "CampaignUpdated")
            .Handle<CampaignDeletedData, CampaignDeleted>(CatalogEvents.CampaignsTopic, "CampaignDeleted"));

        // GET /v1/orders/{id}/events: cada réplica ouve o próprio tópico e avisa os streams abertos nela
        builder.Services.Configure<OrderLiveOptions>(builder.Configuration.GetSection(OrderLiveOptions.Section));
        if (builder.Configuration.GetValue("Kafka:Enabled", defaultValue: true))
        {
            builder.Services.AddHostedService<OrderChangeListener>();
        }
        return builder;
    }
}
