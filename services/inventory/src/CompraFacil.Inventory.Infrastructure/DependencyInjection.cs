using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using Microsoft.Extensions.Hosting;

namespace CompraFacil.Inventory.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco (ConnectionStrings:Default) e demais adaptadores de infraestrutura.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddPostgres<InventoryDbContext>();
        // tópico do contrato (contracts/asyncapi); handlers dos eventos consumidos entram com o serviço
        builder.AddMessaging<InventoryDbContext>("inventory", messaging => messaging.Produces("inventory.reservation.v1"));
        return builder;
    }
}
