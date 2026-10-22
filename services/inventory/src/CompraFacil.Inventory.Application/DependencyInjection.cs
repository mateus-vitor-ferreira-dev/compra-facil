using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Inventory.Application;

public static class DependencyInjection
{
    /// <summary>Casos de uso e validadores (FluentValidation) deste projeto.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        return services;
    }
}
