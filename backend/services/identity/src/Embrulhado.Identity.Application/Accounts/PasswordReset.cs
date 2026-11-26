using System.Text;
using Embrulhado.BuildingBlocks.Messaging;
using Embrulhado.Identity.Domain;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Embrulhado.Identity.Application.Accounts;

/// <summary>Esqueci minha senha (seção Identity:PasswordReset).</summary>
public sealed class PasswordResetOptions
{
    public const string Section = "Identity:PasswordReset";

    /// <summary>Nome do provedor do token no ASP.NET Core Identity.</summary>
    public const string TokenProvider = "PasswordReset";

    /// <summary>Página do front que cria a senha nova; recebe <c>{account_id}</c> e <c>{token}</c>.</summary>
    public string UrlTemplate { get; set; } = "http://localhost:3000/conta/redefinir-senha?conta={account_id}&token={token}";

    /// <summary>Validade do link, independente da do link de confirmação.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Intervalo mínimo entre dois e-mails para a mesma conta: o limite por e-mail. O do gateway é por IP,
    /// e quem troca de IP não enche a caixa de ninguém.
    /// </summary>
    public TimeSpan RequestInterval { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed record RequestPasswordResetCommand(string Email);

/// <summary>
/// Manda o link para criar uma senha nova. Sempre "aceito", exista a conta ou não: a resposta não revela
/// quem tem conta. Convidado não tem senha (o e-mail dele é um marcador que ninguém digita); conta só do
/// Google pode criar uma. O gateway limita as tentativas por IP; aqui, por conta.
/// </summary>
public sealed class RequestPasswordReset(UserManager<User> users, IOutbox outbox, IOptions<PasswordResetOptions> options, TimeProvider clock)
{
    public async Task HandleAsync(RequestPasswordResetCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(command.Email) || command.Email.Length > User.MaxEmailLength)
        {
            return;
        }
        var user = await users.FindByEmailAsync(command.Email.Trim());
        var now = clock.GetUtcNow();
        if (user is null || user.IsGuest || !user.RequestPasswordReset(now, options.Value.RequestInterval))
        {
            return;
        }
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
        var url = new Uri(options.Value.UrlTemplate
            .Replace("{account_id}", user.Id.ToString(), StringComparison.Ordinal)
            .Replace("{token}", token, StringComparison.Ordinal));
        outbox.Add(AccountEvents.Topic, user.Id.ToString(), AccountEvents.PasswordResetRequested,
            new PasswordResetRequestedData(user.Id, user.Email!, user.Name, url, now + options.Value.TokenLifetime));
        // um SaveChanges só (o do UpdateAsync) grava o horário do pedido e o evento juntos. Dois pedidos ao
        // mesmo tempo: o segundo perde no ConcurrencyStamp e não grava nada (o primeiro já mandou o link)
        await users.UpdateAsync(user);
    }
}

public sealed record ResetPasswordCommand(Guid AccountId, string Token, string Password);

public sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(c => c.Token).NotEmpty().MaximumLength(2048);
        // o tamanho mínimo e a variedade de caracteres ficam na política do Identity, como no cadastro
        RuleFor(c => c.Password).NotEmpty().MaximumLength(RegisterValidator.MaxPasswordLength);
    }
}

/// <summary>
/// Cria a senha nova pelo link. O Identity troca o carimbo de segurança junto com a senha, então o token
/// vale uma vez só (e os outros links pendentes da conta também caem). O link prova que a pessoa lê o
/// e-mail: a conta fica confirmada e desbloqueada. Todas as sessões abertas caem (refresh tokens
/// revogados): quem pede a senha nova pode estar tirando alguém da conta. A revogação não trava as
/// famílias, e um refresh no meio dela ainda cria um token que o UPDATE não vê; esse token não troca mais,
/// porque nasceu com o carimbo antigo (<see cref="RefreshToken.MatchesStamp"/>).
/// </summary>
public sealed class ResetPassword(
    IValidator<ResetPasswordCommand> validator,
    UserManager<User> users,
    IRefreshTokenRepository refreshTokens,
    IAccountTransaction transaction,
    TimeProvider clock)
{
    private const string InvalidLink = "link de redefinição inválido ou vencido";

    public async Task HandleAsync(ResetPasswordCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        await validator.ValidateAndThrowAsync(command, ct);

        var user = command.AccountId == Guid.Empty ? null : await users.FindByIdAsync(command.AccountId.ToString());
        string? token = null;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(command.Token));
        }
        catch (FormatException)
        {
            // cai no mesmo erro do token errado
        }
        // conta inexistente, convidado e token errado dão o mesmo erro: o link não revela quem tem conta
        if (user is null || user.IsGuest || token is null)
        {
            throw InvalidLinkError();
        }

        await transaction.RunAsync(async () =>
        {
            Check(await users.ResetPasswordAsync(user, token, command.Password));
            user.EmailConfirmed = true;
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
            Check(await users.UpdateAsync(user));
            await refreshTokens.RevokeAllOfUserAsync(user.Id, clock.GetUtcNow(), RevocationReason.PasswordReset, ct);
            return true;
        }, ct);
    }

    private static void Check(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }
        // o Identity confere o token antes da senha: senha fraca com link bom não gasta o link
        if (result.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal)))
        {
            throw new ValidationException(result.Errors.Select(e => new ValidationFailure(nameof(ResetPasswordCommand.Password), e.Description)));
        }
        throw InvalidLinkError(); // token errado, vencido, já usado (o carimbo mudou) ou corrida com outra troca
    }

    private static ValidationException InvalidLinkError() =>
        new([new ValidationFailure(nameof(ResetPasswordCommand.Token), InvalidLink)]);
}
