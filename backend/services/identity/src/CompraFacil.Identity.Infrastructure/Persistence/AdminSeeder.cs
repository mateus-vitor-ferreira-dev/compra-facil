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
/// A conta só de leitura da operação, para o vigia do agentes-hub: entra com e-mail e senha como o operador, mas
/// só tem o papel <see cref="Roles.Auditor"/>, que abre as listas de /v1/admin e nada que mude algo.
/// </summary>
public sealed class AuditorSeedOptions
{
    public const string Section = "Identity:Auditor";

    public string? Email { get; set; }
    public string? Password { get; set; }
    public string Name { get; set; } = "Agentes (leitura)";
}

/// <summary>
/// Garante que o operador e o auditor configurados existem com o seu papel. A conta criada por aqui mantém a senha
/// depois (trocar a configuração não sobrescreve uma senha alterada). Uma conta que já existia sem o papel não é
/// promovida: o cadastro é público, e quem registrasse antes o e-mail configurado (um auditor ligado depois, um
/// e-mail de operador trocado) ganharia o papel com a própria senha.
/// </summary>
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
        if (user is not null)
        {
            if (!await users.IsInRoleAsync(user, role))
            {
                LogNotPromoted(logger, email, role);
            }
            return;
        }

        user = User.Create(email, name, clock.GetUtcNow());
        user.EmailConfirmed = true; // o e-mail veio da configuração: a conta entra mesmo com a confirmação exigida
        Check(await users.CreateAsync(user, password));
        Check(await users.AddToRoleAsync(user, role));
        LogCreated(logger, email, role);
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

    [LoggerMessage(Level = LogLevel.Error, Message =
        "{Email} já tinha conta sem o papel {Role} e não foi promovida: confira quem criou a conta e dê o papel à mão, " +
        "ou configure outro e-mail")]
    private static partial void LogNotPromoted(ILogger logger, string email, string role);
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
