using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace CompraFacil.Identity.Application.Accounts;

/// <summary>Login com o Google (seção Identity:Google).</summary>
public sealed class GoogleOptions
{
    public const string Section = "Identity:Google";

    /// <summary>Quem emite o ID token; o JWKS vem da discovery dele.</summary>
    public string Authority { get; set; } = "https://accounts.google.com";

    /// <summary>Os client ids aceitos no <c>aud</c>. Vazio desliga o login com o Google.</summary>
    public IList<string> ClientIds { get; } = [];

    public bool Enabled => ClientIds.Count > 0;
}

/// <summary>Quem o Google diz que é: o <c>sub</c> (estável por conta Google), o e-mail e o nome.</summary>
public sealed record GoogleIdentity(string Subject, string Email, string Name);

public interface IGoogleTokenValidator
{
    /// <summary>
    /// Valida o ID token (assinatura pelo JWKS do Google, iss, aud, exp, nonce, email_verified).
    /// </summary>
    /// <exception cref="UnauthorizedException">Token inválido, vencido, de outro app ou com outro nonce.</exception>
    Task<GoogleIdentity> ValidateAsync(string idToken, string nonce, CancellationToken ct);
}

/// <summary>
/// Entrar e se cadastrar com o Google (#180). A conta é achada pelo <c>sub</c> do Google (login externo),
/// nunca só pelo e-mail: um e-mail que já tem conta com senha só é vinculado depois de confirmar a senha,
/// senão quem controlasse um Google com aquele e-mail entraria na conta de outra pessoa.
/// </summary>
public sealed class GoogleSignIn(
    IGoogleTokenValidator validator,
    UserManager<User> users,
    SignInManager<User> signIn,
    Sessions sessions,
    IOutbox outbox,
    IAccountTransaction transaction,
    TimeProvider clock)
{
    public const string Provider = "Google";

    public async Task<IssuedSession> SignInAsync(string idToken, string nonce, CancellationToken ct)
    {
        var google = await validator.ValidateAsync(idToken, nonce, ct);
        if (await users.FindByLoginAsync(Provider, google.Subject) is { } linked)
        {
            return await sessions.StartAsync(linked, ct);
        }
        if (await users.FindByEmailAsync(google.Email) is not null)
        {
            throw new ConflictException(LinkRequiredTitle,
                "já existe uma conta com este e-mail; confirme a senha dela para entrar também com o Google");
        }

        var user = User.Create(google.Email, google.Name, clock.GetUtcNow());
        user.EmailConfirmed = true; // o Google verificou o e-mail (email_verified)
        await transaction.RunAsync(async () =>
        {
            Check(await users.CreateAsync(user)); // sem senha: entra só pelo Google (até criar uma)
            Check(await users.AddToRoleAsync(user, Roles.Customer));
            Check(await users.AddLoginAsync(user, new UserLoginInfo(Provider, google.Subject, Provider)));
            outbox.Add(AccountEvents.Topic, user.Id.ToString(), AccountEvents.AccountRegistered,
                new AccountRegisteredData(user.Id, user.Email!, user.Name, user.CreatedAt));
            return user;
        }, ct);
        return await sessions.StartAsync(user, ct);
    }

    /// <summary>Liga o Google à conta do mesmo e-mail, depois de conferir a senha dela.</summary>
    public async Task<IssuedSession> LinkAsync(string idToken, string nonce, string password, CancellationToken ct)
    {
        var google = await validator.ValidateAsync(idToken, nonce, ct);
        var user = await users.FindByEmailAsync(google.Email)
            ?? throw new UnauthorizedException("não há conta com este e-mail para vincular");
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            var end = await users.GetLockoutEndDateAsync(user);
            throw new AccountLockedException(end is { } e && e > clock.GetUtcNow() ? e - clock.GetUtcNow() : TimeSpan.FromMinutes(1));
        }
        // a senha certa basta: o e-mail da conta é o mesmo que o Google verificou
        if (!result.Succeeded && !(result.IsNotAllowed && await users.CheckPasswordAsync(user, password)))
        {
            throw new UnauthorizedException("senha incorreta");
        }
        if (await users.FindByLoginAsync(Provider, google.Subject) is null)
        {
            Check(await users.AddLoginAsync(user, new UserLoginInfo(Provider, google.Subject, Provider)));
        }
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            Check(await users.UpdateAsync(user));
        }
        return await sessions.StartAsync(user, ct);
    }

    public const string LinkRequiredTitle = "Conta já existe";

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new ConflictException("Conta não criada", string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
