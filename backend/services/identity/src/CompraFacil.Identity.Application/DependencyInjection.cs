using CompraFacil.Identity.Application.Accounts;
using CompraFacil.Identity.Application.Tokens;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CompraFacil.Identity.Application;

public static class DependencyInjection
{
    /// <summary>Casos de uso e validadores (FluentValidation) deste projeto.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<RegisterAccount>();
        services.AddScoped<EmailConfirmationRequests>();
        services.AddScoped<ConfirmEmail>();
        services.AddScoped<ResendConfirmation>();
        services.AddScoped<Sessions>();
        services.AddScoped<GoogleSignIn>();
        services.AddScoped<AccountQueries>();
        services.AddScoped<AccessTokenIssuer>();
        services.TryAddSingleton(TimeProvider.System); // o AddPostgres troca pelo relógio do banco
        return services;
    }
}
