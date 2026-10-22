using CompraFacil.BuildingBlocks.Data;
using Microsoft.Extensions.Hosting;

namespace CompraFacil.Payments.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco (ConnectionStrings:Default) e demais adaptadores de infraestrutura.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddPostgres<PaymentsDbContext>();
        return builder;
    }
}
