using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using Microsoft.Extensions.Hosting;

namespace CompraFacil.Orders.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco (ConnectionStrings:Default) e demais adaptadores de infraestrutura.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddPostgres<OrdersDbContext>();
        // tópico do contrato (contracts/asyncapi); handlers dos eventos consumidos entram com o serviço
        builder.AddMessaging<OrdersDbContext>("orders", messaging => messaging.Produces("orders.order.v1"));
        return builder;
    }
}
