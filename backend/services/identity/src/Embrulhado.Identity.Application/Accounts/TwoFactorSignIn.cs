using Embrulhado.BuildingBlocks.Web;
using Embrulhado.Identity.Domain;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;

namespace Embrulhado.Identity.Application.Accounts;

/// <summary>O que o app autenticador precisa para o cadastro: a chave (para digitar) e o URI do QR code.</summary>
public sealed record AuthenticatorSetup(string SharedKey, string AuthenticatorUri);

/// <summary>2FA ativada: a sessão e os códigos de recuperação, mostrados só desta vez.</summary>
public sealed record EnabledTwoFactor(IssuedSession Session, IReadOnlyList<string> RecoveryCodes);

/// <summary>
/// O segundo fator do login (ameaça T8: conta de operador tomada vira estorno, preço mudado e dados de
/// cliente exportados). TOTP pelo provedor de autenticador do Identity (<see cref="TokenOptions.AuthenticatorTokenProvider"/>,
/// passo atual ±1 e código já usado não vale de novo) ou um código de recuperação. Código errado conta como
/// senha errada (<see cref="UserManager{TUser}.AccessFailedAsync"/>): a quinta bloqueia a conta. O tíquete do
/// primeiro fator é gasto só pelo código certo, uma vez.
/// </summary>
public sealed class TwoFactorSignIn(
    UserManager<User> users,
    IUserStore<User> store,
    MfaTickets tickets,
    Sessions sessions,
    IAccountTransaction transaction)
{
    private const string InvalidCode = "código inválido";

    /// <summary>Segundo passo do login: o código do autenticador ou um de recuperação abre a sessão (com <c>amr</c>).</summary>
    public async Task<IssuedSession> VerifyAsync(string ticket, string? code, string? recoveryCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) == string.IsNullOrWhiteSpace(recoveryCode))
        {
            throw new ValidationException([new ValidationFailure("Code", "envie code ou recovery_code (um dos dois)")]);
        }
        return await CountedAsync(string.IsNullOrWhiteSpace(code) ? "recovery_code" : "totp",
            () => VerifyCoreAsync(ticket, code, recoveryCode, ct));
    }

    private async Task<IssuedSession> VerifyCoreAsync(string ticket, string? code, string? recoveryCode, CancellationToken ct)
    {
        var (found, user) = await tickets.FindAsync(ticket, MfaTicketPurpose.Login, ct);
        if (!user.TwoFactorEnabled)
        {
            throw new UnauthorizedException(MfaTickets.InvalidTicket); // 2FA desligada pelo operador do servidor no meio
        }
        await sessions.EnsureNotLockedAsync(user);

        var valid = !string.IsNullOrWhiteSpace(code)
            ? await VerifyTotpAsync(user, code)
            : await RedeemRecoveryCodeAsync(user, recoveryCode!);
        if (!valid)
        {
            await FailAsync(user, string.IsNullOrWhiteSpace(code) ? "RecoveryCode" : "Code");
        }

        return await transaction.RunAsync(async () =>
        {
            await tickets.ConsumeAsync(found, ct);
            await users.ResetAccessFailedCountAsync(user);
            return await sessions.StartAsync(user, ct, mfa: true);
        }, ct);
    }

    /// <summary>
    /// Cadastro, passo 1: uma chave nova a cada chamada. A chave só passa a valer com o primeiro código certo
    /// (<see cref="ConfirmSetupAsync"/>); quem tivesse lido uma chave anterior não fica com ela.
    /// </summary>
    public async Task<AuthenticatorSetup> BeginSetupAsync(string ticket, CancellationToken ct)
    {
        var (_, user) = await tickets.FindAsync(ticket, MfaTicketPurpose.Setup, ct);
        if (user.TwoFactorEnabled)
        {
            throw new UnauthorizedException(MfaTickets.InvalidTicket);
        }
        await sessions.EnsureNotLockedAsync(user);
        var key = users.GenerateNewAuthenticatorKey();
        // pelo store: o ResetAuthenticatorKeyAsync do UserManager trocaria o carimbo de segurança e derrubaria o tíquete
        await ((IUserAuthenticatorKeyStore<User>)store).SetAuthenticatorKeyAsync(user, key, ct);
        Check(await users.RemoveAuthenticationTokenAsync(user, Totp.LastStepProvider, Totp.LastStepName)); // grava a chave junto
        return new AuthenticatorSetup(Totp.Grouped(key), Totp.AuthenticatorUri(user.Email!, key));
    }

    /// <summary>
    /// Cadastro, passo 2: o primeiro código certo liga a 2FA, gera os códigos de recuperação e abre a sessão
    /// (com <c>amr</c>). Ligar a 2FA troca o carimbo de segurança: sessões antigas da conta caem.
    /// </summary>
    public Task<EnabledTwoFactor> ConfirmSetupAsync(string ticket, string code, CancellationToken ct) =>
        CountedAsync("setup", () => ConfirmSetupCoreAsync(ticket, code, ct));

    private async Task<EnabledTwoFactor> ConfirmSetupCoreAsync(string ticket, string code, CancellationToken ct)
    {
        var (found, user) = await tickets.FindAsync(ticket, MfaTicketPurpose.Setup, ct);
        if (user.TwoFactorEnabled || await users.GetAuthenticatorKeyAsync(user) is null)
        {
            throw new UnauthorizedException(MfaTickets.InvalidTicket);
        }
        await sessions.EnsureNotLockedAsync(user);
        if (!await VerifyTotpAsync(user, code))
        {
            await FailAsync(user, "Code");
        }

        return await transaction.RunAsync(async () =>
        {
            await tickets.ConsumeAsync(found, ct);
            Check(await users.SetTwoFactorEnabledAsync(user, true));
            await users.ResetAccessFailedCountAsync(user);
            var codes = await ReplaceRecoveryCodesAsync(user, ct);
            return new EnabledTwoFactor(await sessions.StartAsync(user, ct, mfa: true), codes);
        }, ct);
    }

    /// <summary>Códigos de recuperação novos (os anteriores deixam de valer), com um código atual do autenticador.</summary>
    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(Guid userId, string code, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("conta não encontrada");
        if (!user.TwoFactorEnabled)
        {
            throw new ConflictException("Autenticação em duas etapas desligada", "a conta não tem a autenticação em duas etapas ativa");
        }
        await sessions.EnsureNotLockedAsync(user);
        if (!await VerifyTotpAsync(user, code))
        {
            await FailAsync(user, "Code");
        }
        await users.ResetAccessFailedCountAsync(user);
        return await ReplaceRecoveryCodesAsync(user, ct);
    }

    // o resultado do segundo passo nas métricas (IdentityMetrics.MfaVerification)
    private static async Task<T> CountedAsync<T>(string method, Func<Task<T>> step)
    {
        try
        {
            var result = await step();
            IdentityMetrics.MfaVerification(method, "succeeded");
            return result;
        }
        catch (ValidationException)
        {
            IdentityMetrics.MfaVerification(method, "invalid_code");
            throw;
        }
        catch (AccountLockedException)
        {
            IdentityMetrics.MfaVerification(method, "locked_out");
            throw;
        }
        catch (UnauthorizedException)
        {
            IdentityMetrics.MfaVerification(method, "invalid_ticket");
            throw;
        }
    }

    private async Task<bool> VerifyTotpAsync(User user, string code) =>
        Totp.Normalize(code) is { } digits
        && await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, digits);

    private async Task<bool> RedeemRecoveryCodeAsync(User user, string recoveryCode) =>
        RecoveryCodes.LooksValid(recoveryCode)
        && (await users.RedeemTwoFactorRecoveryCodeAsync(user, RecoveryCodes.Hash(recoveryCode))).Succeeded;

    /// <summary>Conta a tentativa errada; a quinta já responde 423, como a senha errada.</summary>
    private async Task FailAsync(User user, string field)
    {
        await users.AccessFailedAsync(user);
        await sessions.EnsureNotLockedAsync(user);
        throw new ValidationException([new ValidationFailure(field, InvalidCode)]);
    }

    private async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(User user, CancellationToken ct)
    {
        var codes = RecoveryCodes.New();
        // o store do Identity guarda o que receber em texto puro: recebe só os hashes
        await ((IUserTwoFactorRecoveryCodeStore<User>)store).ReplaceCodesAsync(user, codes.Select(RecoveryCodes.Hash), ct);
        Check(await users.UpdateAsync(user));
        return codes;
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            // a conta mudou no meio (outra requisição com o mesmo código, por exemplo): tenta de novo
            throw new ConflictException("Conta alterada", string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
