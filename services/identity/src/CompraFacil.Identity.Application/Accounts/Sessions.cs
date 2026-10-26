using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Identity.Application.Tokens;
using CompraFacil.Identity.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CompraFacil.Identity.Application.Accounts;

public sealed record LoginCommand(string Email, string Password);

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(c => c.Email).NotEmpty().MaximumLength(User.MaxEmailLength);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(RegisterValidator.MaxPasswordLength);
    }
}

/// <summary>
/// Login, refresh e logout. O login dá um access token curto (JWT) e um refresh token longo; cada
/// refresh troca o refresh token por outro (rotação) e um token já trocado, apresentado de novo,
/// derruba a sessão inteira. Erro de credencial é sempre o mesmo 401, exista a conta ou não.
/// </summary>
public sealed class Sessions(
    IValidator<LoginCommand> validator,
    UserManager<User> users,
    SignInManager<User> signIn,
    IRefreshTokenRepository refreshTokens,
    AccessTokenIssuer accessTokens,
    IOptions<JwtOptions> options,
    TimeProvider clock)
{
    private const string InvalidCredentials = "e-mail ou senha inválidos";
    private const string InvalidRefreshToken = "refresh token inválido, expirado ou já usado";

    // hash de uma senha qualquer: o login de e-mail inexistente gasta o mesmo tempo que o de senha
    // errada, e o tempo de resposta não revela quem tem conta
    private static readonly Lazy<string> s_dummyHash = new(() => new PasswordHasher<User>().HashPassword(new User(), Guid.NewGuid().ToString()));

    public async Task<IssuedSession> LoginAsync(LoginCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        await validator.ValidateAndThrowAsync(command, ct);

        var user = await users.FindByEmailAsync(command.Email.Trim());
        if (user is null)
        {
            users.PasswordHasher.VerifyHashedPassword(new User(), s_dummyHash.Value, command.Password);
            throw new UnauthorizedException(InvalidCredentials);
        }

        var result = await signIn.CheckPasswordSignInAsync(user, command.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            var end = await users.GetLockoutEndDateAsync(user);
            var retryAfter = end is { } e && e > clock.GetUtcNow() ? e - clock.GetUtcNow() : TimeSpan.FromMinutes(1);
            throw new AccountLockedException(retryAfter);
        }
        if (!result.Succeeded)
        {
            throw new UnauthorizedException(InvalidCredentials);
        }

        var refreshToken = RefreshTokenValue.New();
        refreshTokens.Add(RefreshToken.Issue(user.Id, RefreshTokenValue.Hash(refreshToken), clock.GetUtcNow(), options.Value.RefreshTokenLifetime));
        await refreshTokens.SaveChangesAsync(ct);
        return await SessionAsync(user, refreshToken, ct);
    }

    public async Task<IssuedSession> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var current = await FindAsync(refreshToken, ct) ?? throw new UnauthorizedException(InvalidRefreshToken);
        var now = clock.GetUtcNow();
        if (current.WasRotated)
        {
            // o token já tinha sido trocado: alguém tem uma cópia; nenhum token da sessão vale mais
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, RevocationReason.Reuse, ct);
            throw new UnauthorizedException(InvalidRefreshToken);
        }
        if (!current.IsActive(now))
        {
            throw new UnauthorizedException(InvalidRefreshToken);
        }

        var user = await users.FindByIdAsync(current.UserId.ToString());
        if (user is null || await users.IsLockedOutAsync(user))
        {
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, RevocationReason.Logout, ct);
            throw new UnauthorizedException(InvalidRefreshToken);
        }

        var nextToken = RefreshTokenValue.New();
        refreshTokens.Add(current.Rotate(RefreshTokenValue.Hash(nextToken), now, options.Value.RefreshTokenLifetime));
        try
        {
            await refreshTokens.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            throw new UnauthorizedException(InvalidRefreshToken); // outro refresh com o mesmo token chegou antes
        }
        return await SessionAsync(user, nextToken, ct);
    }

    /// <summary>Logout: derruba a sessão do refresh token. Token desconhecido não é erro (idempotente).</summary>
    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        if (await FindAsync(refreshToken, ct) is { } token)
        {
            await refreshTokens.RevokeFamilyAsync(token.FamilyId, clock.GetUtcNow(), RevocationReason.Logout, ct);
        }
    }

    private async Task<RefreshToken?> FindAsync(string refreshToken, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > RefreshTokenValue.MaxLength
            ? null
            : await refreshTokens.FindByHashAsync(RefreshTokenValue.Hash(refreshToken), ct);

    private async Task<IssuedSession> SessionAsync(User user, string refreshToken, CancellationToken ct)
    {
        var accessToken = await accessTokens.IssueAsync(user, [.. await users.GetRolesAsync(user)], ct);
        return new IssuedSession(accessToken, options.Value.AccessTokenLifetime, refreshToken, options.Value.RefreshTokenLifetime);
    }
}
