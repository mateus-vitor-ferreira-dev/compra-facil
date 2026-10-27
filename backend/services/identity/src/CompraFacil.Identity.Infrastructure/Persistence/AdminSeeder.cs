using CompraFacil.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompraFacil.Identity.Infrastructure.Persistence;

/// <summary>Operador inicial (seção Identity:Admin). Sem e-mail e senha configurados, não faz nada.</summary>
public sealed class AdminSeedOptions
{
    public const string Section = "Identity:Admin";

    public string? Email { get; set; }
    public string? Password { get; set; }
    public string Name { get; set; } = "Operador";
}

/// <summary>
/// Garante que o operador configurado existe e tem o papel <c>admin</c>. Conta já existente mantém a
/// senha: trocar a configuração não sobrescreve uma senha alterada depois.
/// </summary>
public sealed partial class AdminSeeder(
    UserManager<User> users, IOptions<AdminSeedOptions> options, TimeProvider clock, ILogger<AdminSeeder> logger)
{
    public async Task SeedAsync()
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Email) || string.IsNullOrWhiteSpace(o.Password))
        {
            return;
        }

        var user = await users.FindByEmailAsync(o.Email);
        if (user is null)
        {
            user = User.Create(o.Email, o.Name, clock.GetUtcNow());
            user.EmailConfirmed = true; // o e-mail veio da configuração: o operador entra mesmo com a confirmação exigida
            Check(await users.CreateAsync(user, o.Password));
            LogCreated(logger, o.Email);
        }
        if (!await users.IsInRoleAsync(user, Roles.Admin))
        {
            Check(await users.AddToRoleAsync(user, Roles.Admin));
        }
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Identity:Admin inválido: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "operador inicial {Email} criado")]
    private static partial void LogCreated(ILogger logger, string email);
}

public static class AdminSeederExtensions
{
    public static async Task SeedAdminAsync(this IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync();
    }
}
