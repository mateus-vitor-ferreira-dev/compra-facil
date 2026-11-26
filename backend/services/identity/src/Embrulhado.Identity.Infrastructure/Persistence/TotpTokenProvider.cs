using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Embrulhado.Identity.Application.Accounts;
using Microsoft.AspNetCore.Identity;

namespace Embrulhado.Identity.Infrastructure.Persistence;

/// <summary>
/// O provedor de autenticador (TOTP) no lugar do <see cref="AuthenticatorTokenProvider{TUser}"/> do Identity,
/// que aceita ±2 passos (90 s para cada lado), usa o relógio da máquina e deixa o mesmo código valer de novo
/// enquanto ele estiver na janela. Este aceita o passo atual ±1 (o relógio do celular adiantado ou atrasado até
/// 30 s), usa o relógio do serviço e guarda o último passo aceito: um código visto por cima do ombro, ou
/// interceptado, não abre uma segunda sessão. A chave é a do Identity (Base32 na user_tokens).
/// </summary>
internal sealed class TotpTokenProvider<TUser>(TimeProvider clock) : IUserTwoFactorTokenProvider<TUser>
    where TUser : class
{
    public const string Name = "EmbrulhadoTotp";

    // quem gera o código é o app no celular da pessoa
    public Task<string> GenerateAsync(string purpose, UserManager<TUser> manager, TUser user) => Task.FromResult("");

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<TUser> manager, TUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        var key = await manager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key) || Totp.Normalize(token) is not { } code)
        {
            return false;
        }

        var keyBytes = Totp.FromBase32(key);
        var now = Totp.StepAt(clock.GetUtcNow());
        long? matched = null;
        for (var step = now - 1; step <= now + 1; step++)
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Totp.Code(keyBytes, step)), Encoding.ASCII.GetBytes(code)))
            {
                matched = step;
            }
        }
        if (matched is not { } accepted)
        {
            return false;
        }

        var last = await manager.GetAuthenticationTokenAsync(user, Totp.LastStepProvider, Totp.LastStepName);
        if (long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var lastStep) && accepted <= lastStep)
        {
            return false; // o mesmo código (ou um anterior a ele) já entrou
        }
        // grava com o ConcurrencyStamp da conta: de duas requisições com o mesmo código, a segunda falha aqui
        var saved = await manager.SetAuthenticationTokenAsync(user, Totp.LastStepProvider, Totp.LastStepName,
            accepted.ToString(CultureInfo.InvariantCulture));
        return saved.Succeeded;
    }

    public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<TUser> manager, TUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return !string.IsNullOrEmpty(await manager.GetAuthenticatorKeyAsync(user));
    }
}
