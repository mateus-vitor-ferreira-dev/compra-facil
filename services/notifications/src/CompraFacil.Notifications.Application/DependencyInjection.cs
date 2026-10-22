using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompraFacil.Notifications.Application;

public static class DependencyInjection
{
    /// <summary>Casos de uso e validadores (FluentValidation) deste projeto.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.TryAddSingleton(TimeProvider.System); // o AddPostgres troca pelo relógio do banco
        return services;
    }
}
