using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Identity.Application.Accounts;
using CompraFacil.Identity.Application.Tokens;
using CompraFacil.Identity.Domain;
using CompraFacil.Identity.Infrastructure.Google;
using CompraFacil.Identity.Infrastructure.Keys;
using CompraFacil.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CompraFacil.Identity.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Banco, ASP.NET Core Identity, chaves de assinatura e o admin inicial.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        builder.AddPostgres<IdentityDbContext>();
        builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        builder.Services.AddScoped<IAccountTransaction, AccountTransaction>();

        var confirmation = builder.Configuration.GetSection(EmailConfirmationOptions.Section);
        builder.Services.Configure<EmailConfirmationOptions>(confirmation);
        var confirmationOptions = confirmation.Get<EmailConfirmationOptions>() ?? new EmailConfirmationOptions();
        builder.Services.AddIdentityCore<User>(o =>
            {
                o.SignIn.RequireConfirmedEmail = confirmationOptions.Required;
                o.User.RequireUniqueEmail = true;
                o.User.AllowedUserNameCharacters = ""; // o login é o e-mail, já validado no cadastro
                // comprimento acima de regras de composição (NIST SP 800-63B)
                o.Password.RequiredLength = 8;
                o.Password.RequiredUniqueChars = 4;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders() // token do link de confirmação (Data Protection)
            .AddErrorDescriber<PortugueseIdentityErrorDescriber>();
        builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = confirmationOptions.TokenLifetime);

        // cifra as chaves privadas de assinatura; o anel do Data Protection fica no mesmo banco, então
        // todas as réplicas decifram. Em produção, proteger o anel com um certificado ou KMS (F9).
        builder.Services.AddDataProtection()
            .SetApplicationName("compra-facil-identity")
            .PersistKeysToDbContext<IdentityDbContext>();

        builder.Services.AddOptions<JwtOptions>()
            .Bind(builder.Configuration.GetSection(JwtOptions.Section))
            .Validate(o => { o.RotationPolicy.Validate(); return true; })
            .ValidateOnStart();
        builder.Services.AddSingleton<DbSigningKeyRing>();
        builder.Services.AddSingleton<ISigningKeyRing>(sp => sp.GetRequiredService<DbSigningKeyRing>());

        // contracts/asyncapi/identity.v1.yaml: só publica (o outbox garante que conta e evento saem juntos)
        builder.AddMessaging<IdentityDbContext>("identity", m => m.Produces(AccountEvents.Topic));

        // login com o Google (#180): o ID token vem do BFF e é validado pelo JWKS do Google
        builder.Services.Configure<GoogleOptions>(builder.Configuration.GetSection(GoogleOptions.Section));
        builder.Services.AddHttpClient(GoogleTokenValidator.HttpClientName);
        builder.Services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();

        builder.Services.Configure<AdminSeedOptions>(builder.Configuration.GetSection(AdminSeedOptions.Section));
        builder.Services.AddScoped<AdminSeeder>();
        return builder;
    }
}
