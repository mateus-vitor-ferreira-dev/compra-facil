using CompraFacil.BuildingBlocks.Data;
using Microsoft.Extensions.Hosting;

namespace CompraFacil.Orders.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco (ConnectionStrings:Default) e demais adaptadores de infraestrutura.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddPostgres<OrdersDbContext>();
        return builder;
    }
}
