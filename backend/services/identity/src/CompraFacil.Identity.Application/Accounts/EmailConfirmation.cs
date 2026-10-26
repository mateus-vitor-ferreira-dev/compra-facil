using System.Text;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.Identity.Domain;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace CompraFacil.Identity.Application.Accounts;

/// <summary>Confirmação de e-mail (seção Identity:EmailConfirmation).</summary>
public sealed class EmailConfirmationOptions
{
    public const string Section = "Identity:EmailConfirmation";

    /// <summary>Login só com e-mail confirmado (403 antes disso). Desligado no ambiente local.</summary>
    public bool Required { get; set; }

    /// <summary>Página do front que confirma; recebe <c>{account_id}</c> e <c>{token}</c>.</summary>
    public string UrlTemplate { get; set; } = "http://localhost:3000/conta/confirmar-email?conta={account_id}&token={token}";

    /// <summary>Validade do link (o token do ASP.NET Core Identity).</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromDays(1);
}

/// <summary>
/// Gera o token do Identity e põe o pedido de confirmação no outbox. Quem chama faz o SaveChanges: o
/// evento sai na mesma transação da escrita que o motivou (cadastro ou reenvio).
/// </summary>
public sealed class EmailConfirmationRequests(
    UserManager<User> users, IOutbox outbox, IOptions<EmailConfirmationOptions> options, TimeProvider clock)
{
    public async Task EnqueueAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(user)));
        var url = new Uri(options.Value.UrlTemplate
            .Replace("{account_id}", user.Id.ToString(), StringComparison.Ordinal)
            .Replace("{token}", token, StringComparison.Ordinal));
        outbox.Add(AccountEvents.Topic, user.Id.ToString(), AccountEvents.EmailConfirmationRequested,
            new EmailConfirmationRequestedData(user.Id, user.Email!, user.Name, url, clock.GetUtcNow() + options.Value.TokenLifetime));
    }
}

public sealed record ConfirmEmailCommand(Guid AccountId, string Token);

/// <summary>Confirma o e-mail pelo link. Já confirmado: nada a fazer (o link pode ser clicado duas vezes).</summary>
public sealed class ConfirmEmail(UserManager<User> users)
{
    public async Task HandleAsync(ConfirmEmailCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ct.ThrowIfCancellationRequested();
        var user = command.AccountId == Guid.Empty ? null : await users.FindByIdAsync(command.AccountId.ToString());
        if (user is { EmailConfirmed: true })
        {
            return;
        }
        string? token = null;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(command.Token));
        }
        catch (FormatException)
        {
            // cai no mesmo erro do token errado
        }
        // conta inexistente e token errado dão o mesmo erro: o link não revela quem tem conta
        if (user is null || token is null || !(await users.ConfirmEmailAsync(user, token)).Succeeded)
        {
            throw new ValidationException([new ValidationFailure(nameof(command.Token), "link de confirmação inválido ou vencido")]);
        }
    }
}

public sealed record ResendConfirmationCommand(string Email);

/// <summary>
/// Reenvia o link. Sempre "aceito", exista a conta ou não, confirmada ou não: a resposta não revela
/// quem tem conta. O gateway limita as tentativas por IP.
/// </summary>
public sealed class ResendConfirmation(UserManager<User> users, EmailConfirmationRequests requests, IAccountTransaction transaction)
{
    public async Task HandleAsync(ResendConfirmationCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Email) || command.Email.Length > User.MaxEmailLength)
        {
            return;
        }
        var user = await users.FindByEmailAsync(command.Email.Trim());
        if (user is null || user.EmailConfirmed)
        {
            return;
        }
        await transaction.RunAsync(async () =>
        {
            await requests.EnqueueAsync(user);
            return true;
        }, ct);
    }
}
