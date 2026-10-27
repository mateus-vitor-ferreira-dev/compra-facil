using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Identity.Domain;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;

namespace CompraFacil.Identity.Application.Accounts;

public sealed record RegisterCommand(string Email, string Password, string Name);

public sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public const int MaxPasswordLength = 128;

    public RegisterValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(User.MaxEmailLength);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(User.MaxNameLength);
        // o tamanho mínimo e a variedade de caracteres ficam na política do Identity (IdentitySetup)
        RuleFor(c => c.Password).NotEmpty().MaximumLength(MaxPasswordLength);
    }
}

/// <summary>
/// Cadastro de cliente: cria a conta com o papel <c>customer</c> e publica AccountRegistered e o pedido
/// de confirmação de e-mail, tudo numa transação (outbox): ou a conta e os eventos, ou nada.
/// </summary>
public sealed class RegisterAccount(
    IValidator<RegisterCommand> validator,
    UserManager<User> users,
    IOutbox outbox,
    EmailConfirmationRequests confirmations,
    IAccountTransaction transaction,
    TimeProvider clock)
{
    public async Task<AccountView> HandleAsync(RegisterCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        await validator.ValidateAndThrowAsync(command, ct);

        var user = User.Create(command.Email, command.Name, clock.GetUtcNow());
        await transaction.RunAsync(async () =>
        {
            Check(await users.CreateAsync(user, command.Password));
            Check(await users.AddToRoleAsync(user, Roles.Customer));
            outbox.Add(AccountEvents.Topic, user.Id.ToString(), AccountEvents.AccountRegistered,
                new AccountRegisteredData(user.Id, user.Email!, user.Name, user.CreatedAt));
            await confirmations.EnqueueAsync(user);
            return user;
        }, ct);
        return AccountView.From(user, [Roles.Customer]);
    }

    private static void Check(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }
        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            throw new ConflictException("E-mail já cadastrado", "já existe uma conta com este e-mail");
        }
        // o resto é da política de senha ou do formato do e-mail: 422 no campo certo
        throw new ValidationException(result.Errors.Select(e => new ValidationFailure(
            e.Code.StartsWith("Password", StringComparison.Ordinal) ? nameof(RegisterCommand.Password) : nameof(RegisterCommand.Email),
            e.Description)));
    }
}
