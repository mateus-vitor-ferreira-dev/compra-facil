using CompraFacil.Identity.Application.Accounts;
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

    /// <summary>
    /// Só no ambiente local e no CI (compose): a chave TOTP (Base32) do operador, para os scripts (k6,
    /// <c>task token</c>) entrarem com o segundo fator sem um celular. Vale só para a conta que ainda não tem
    /// 2FA e não gera códigos de recuperação. Em produção fica vazia: o operador cadastra o próprio app no
    /// primeiro login.
    /// </summary>
    public string? TotpKey { get; set; }
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
    UserManager<User> users, IUserStore<User> store, IOptions<AdminSeedOptions> options, IOptions<AuditorSeedOptions> auditor,
    TimeProvider clock, ILogger<AdminSeeder> logger)
{
    public async Task SeedAsync()
    {
        var o = options.Value;
        await SeedAsync(o.Email, o.Password, o.Name, Roles.Admin);
        await SeedTotpKeyAsync(o.Email, o.TotpKey);
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

    // a chave fixa do ambiente local: liga a 2FA do operador que ainda não a tem
    private async Task SeedTotpKeyAsync(string? email, string? key)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(key))
        {
            return;
        }
        if (Totp.FromBase32(key).Length < 16) // 128 bits; FormatException se nem for Base32
        {
            throw new InvalidOperationException("Identity:Admin:TotpKey precisa de pelo menos 26 caracteres Base32 (128 bits)");
        }
        var user = await users.FindByEmailAsync(email);
        if (user is null || user.TwoFactorEnabled || !await users.IsInRoleAsync(user, Roles.Admin))
        {
            return;
        }
        await ((IUserAuthenticatorKeyStore<User>)store).SetAuthenticatorKeyAsync(user, key.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant(), CancellationToken.None);
        Check(await users.SetTwoFactorEnabledAsync(user, true));
        LogTotpSeeded(logger, email);
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Identity:Admin ou Identity:Auditor inválido: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "2FA do operador {Email} ligada com a chave fixa de Identity:Admin:TotpKey: só para o ambiente local e o CI")]
    private static partial void LogTotpSeeded(ILogger logger, string email);

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
