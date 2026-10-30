using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompraFacil.Fulfillment.Application;

public static class DependencyInjection
{
    /// <summary>Casos de uso e validadores (FluentValidation) deste projeto.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.TryAddSingleton(TimeProvider.System); // o AddPostgres troca pelo relógio do banco
        services.AddScoped<SupplierOrderPlacer>();
        services.AddScoped<Invoices.ReturnInvoiceIssuer>();
        services.AddScoped<SupplierUpdates>();
        services.AddScoped<OverdueOrders>();
        services.AddScoped<SupplierOrderActions>();
        services.AddScoped<OrderMargins>();
        services.AddScoped<SupplierReturnOpener>();
        services.AddScoped<SupplierReturnUpdates>();
        services.AddScoped<PendingSupplierReturns>();
        services.AddScoped<CatalogSync>();
        services.AddScoped<SupplierStock>();
        services.AddScoped<Shipping.ShippingQuotes>();
        return services;
    }
}
