using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using Microsoft.Extensions.Hosting;

namespace CompraFacil.Payments.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco (ConnectionStrings:Default) e demais adaptadores de infraestrutura.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddPostgres<PaymentsDbContext>();
        // tópico do contrato (contracts/asyncapi); handlers dos eventos consumidos entram com o serviço
        builder.AddMessaging<PaymentsDbContext>("payments", messaging => messaging.Produces("payments.payment.v1"));
        return builder;
    }
}
