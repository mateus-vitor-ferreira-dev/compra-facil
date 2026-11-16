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
/// <summary>
/// A conta só de leitura da operação (#403), para o vigia do agentes-hub: entra com e-mail e senha como o
/// operador, mas só tem o papel <see cref="Roles.Auditor"/>, que abre as listas de /v1/admin e nada que mude algo.
/// </summary>
public sealed class AuditorSeedOptions
{
    public const string Section = "Identity:Auditor";

    public string? Email { get; set; }
    public string? Password { get; set; }
    public string Name { get; set; } = "Agentes (leitura)";
}

public sealed partial class AdminSeeder(
    UserManager<User> users, IOptions<AdminSeedOptions> options, IOptions<AuditorSeedOptions> auditor, TimeProvider clock,
    ILogger<AdminSeeder> logger)
{
    public async Task SeedAsync()
    {
        var o = options.Value;
        await SeedAsync(o.Email, o.Password, o.Name, Roles.Admin);
        var a = auditor.Value;
        await SeedAsync(a.Email, a.Password, a.Name, Roles.Auditor);
    }

    private async Task SeedAsync(string? email, string? password, string name, string role)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = User.Create(email, name, clock.GetUtcNow());
            user.EmailConfirmed = true; // o e-mail veio da configuração: a conta entra mesmo com a confirmação exigida
            Check(await users.CreateAsync(user, password));
            LogCreated(logger, email, role);
        }
        if (!await users.IsInRoleAsync(user, role))
        {
            Check(await users.AddToRoleAsync(user, role));
        }
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Identity:Admin ou Identity:Auditor inválido: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "conta inicial {Email} criada com o papel {Role}")]
    private static partial void LogCreated(ILogger logger, string email, string role);
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
