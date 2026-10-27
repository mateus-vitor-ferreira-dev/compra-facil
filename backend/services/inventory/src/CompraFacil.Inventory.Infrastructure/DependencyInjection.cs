using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Inventory.Application.Stock;
using CompraFacil.Inventory.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CompraFacil.Inventory.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco e mensageria do inventory.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddPostgres<InventoryDbContext>();
        builder.Services.AddScoped<IStockRepository, StockRepository>();
        builder.Services.Configure<InventoryOptions>(builder.Configuration.GetSection("Inventory"));

        // contracts/asyncapi/inventory.v1.yaml: publica inventory.reservation.v1; consome a SAGA e o catálogo
        builder.AddMessaging<InventoryDbContext>("inventory", m => m
            .Produces(InventoryEvents.Topic)
            .Handle<OrderPlacedData, ReserveStock>(InventoryEvents.OrdersTopic, "OrderPlaced")
            .Handle<OrderConfirmedData, CommitReservation>(InventoryEvents.OrdersTopic, "OrderConfirmed")
            .Handle<OrderCancelledData, ReleaseReservation>(InventoryEvents.OrdersTopic, "OrderCancelled")
            .Handle<ProductCreatedData, ProductCreated>(InventoryEvents.ProductsTopic, "ProductCreated")
            .Handle<ProductDeletedData, ProductDeleted>(InventoryEvents.ProductsTopic, "ProductDeleted"));
        return builder;
    }
}
