using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Fulfillment.Application.Invoices;
using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using CompraFacil.Fulfillment.Infrastructure.Invoices;
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
        builder.Services.AddScoped<ISupplierProductRepository, SupplierProductRepository>();

        var supplier = builder.Configuration.GetSection("Supplier");
        builder.Services.Configure<SupplierOptions>(supplier);
        builder.Services.Configure<SupplierDeadlines>(builder.Configuration.GetSection(SupplierDeadlines.Section));
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
        // a nota fiscal (#221): emitida pelo despachante antes do fornecedor, cancelada no estorno
        var invoice = builder.Configuration.GetSection("Invoice");
        builder.Services.Configure<InvoiceOptions>(invoice);
        builder.Services.AddSingleton(invoice.Get<InvoiceSettings>() ?? new InvoiceSettings());
        builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        builder.Services.AddScoped<InvoiceCanceller>();
        builder.Services.AddSingleton<NuvemFiscalTokenCache>();
        builder.Services.AddHttpClient(NuvemFiscalTokenCache.ClientName, (sp, http) =>
            http.Timeout = sp.GetRequiredService<IOptions<InvoiceOptions>>().Value.Timeout);
        builder.Services.AddHttpClient<IInvoiceIssuer, NuvemFiscalInvoiceIssuer>((sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<InvoiceOptions>>().Value;
            http.BaseAddress = o.BaseUrl;
            http.Timeout = o.Timeout;
        });
        if (invoice.GetValue("CancellerEnabled", defaultValue: true))
        {
            builder.Services.AddHostedService<InvoiceCancellerJob>();
        }

        if (supplier.GetValue("DispatcherEnabled", defaultValue: true))
        {
            builder.Services.AddHostedService<SupplierDispatcher>();
        }
        if (supplier.GetValue("CatalogSyncEnabled", defaultValue: true))
        {
            builder.Services.AddHostedService<CatalogSyncJob>();
        }

        // contracts/asyncapi/fulfillment.v1.yaml: o pedido confirmado vai ao fornecedor; o que ele conta vira
        // evento, e o feed dele também
        builder.AddMessaging<FulfillmentDbContext>("fulfillment", m => m
            .Produces(FulfillmentEvents.Topic)
            .Produces(SupplierCatalogEvents.Topic)
            .Produces(InvoiceEvents.Topic)
            .Handle<OrderConfirmedData, OnOrderConfirmed>(FulfillmentEvents.OrdersTopic, FulfillmentEvents.OrderConfirmed)
            .Handle<PaymentRefundedData, OnPaymentRefunded>(InvoiceEvents.PaymentsTopic, InvoiceEvents.PaymentRefunded));
        return builder;
    }
}
