using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using Microsoft.Extensions.Hosting;

namespace CompraFacil.Notifications.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco (ConnectionStrings:Default) e demais adaptadores de infraestrutura.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddPostgres<NotificationsDbContext>();
        // só consome (contracts/asyncapi/notifications.v1.yaml); handlers entram com o serviço (#77)
        builder.AddMessaging<NotificationsDbContext>("notifications", _ => { });
        return builder;
    }
}
