using Embrulhado.BuildingBlocks.Web;
using Embrulhado.Identity.Application.Tokens;
using Embrulhado.Identity.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Embrulhado.Identity.Application.Accounts;

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
/// derruba a sessão inteira. Dentro do intervalo de reuso (segundos depois da troca), o mesmo token
/// ainda dá um par novo da sessão: são requisições paralelas do mesmo navegador, não uma cópia. Uma
/// sessão aberta antes da última troca de senha (outro carimbo de segurança) não troca mais. Erro de
/// credencial é sempre o mesmo 401, exista a conta ou não.
/// <para>
/// Segundo fator (ameaça T8): conta com 2FA ativa, ou com o papel <see cref="Roles.Admin"/>, nunca recebe
/// sessão só com a senha (ou só com o Google). O primeiro fator devolve um tíquete (<see cref="MfaChallenge"/>);
/// a sessão sai em <see cref="TwoFactorSignIn"/>, com o claim <c>amr</c> = <c>mfa</c> no access token, e o
/// refresh repete o claim só na família que nasceu do segundo fator. O auditor fica fora: é um robô que
/// entra com senha e só lê (#403).
/// </para>
/// </summary>
public sealed class Sessions(
    IValidator<LoginCommand> validator,
    UserManager<User> users,
    SignInManager<User> signIn,
    IRefreshTokenRepository refreshTokens,
    MfaTickets mfaTickets,
    IAccountTransaction transaction,
    AccessTokenIssuer accessTokens,
    IOptions<JwtOptions> options,
    TimeProvider clock)
{
    private const string InvalidCredentials = "e-mail ou senha inválidos";
    private const string InvalidRefreshToken = "refresh token inválido, expirado ou já usado";

    // hash de uma senha qualquer: o login de e-mail inexistente gasta o mesmo tempo que o de senha
    // errada, e o tempo de resposta não revela quem tem conta
    private static readonly Lazy<string> s_dummyHash = new(() => new PasswordHasher<User>().HashPassword(new User(), Guid.NewGuid().ToString()));

    public async Task<SignInOutcome> LoginAsync(LoginCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        await validator.ValidateAndThrowAsync(command, ct);
        try
        {
            var outcome = await CheckAndStartAsync(command, ct);
            // a senha conferiu: sessão aberta (succeeded) ou o segundo fator pendente (mfa_required,
            // mfa_setup_required). O segundo passo conta em IdentityMetrics.MfaVerification
            IdentityMetrics.Login(outcome.Challenge?.Status ?? "succeeded");
            return outcome;
        }
        catch (UnauthorizedException)
        {
            IdentityMetrics.Login("invalid_credentials");
            throw;
        }
        catch (AccountLockedException)
        {
            IdentityMetrics.Login("locked_out");
            throw;
        }
        catch (ForbiddenException)
        {
            IdentityMetrics.Login("not_confirmed");
            throw;
        }
    }

    /// <summary>O login em si; quem chama conta o resultado (IdentityMetrics).</summary>
    private async Task<SignInOutcome> CheckAndStartAsync(LoginCommand command, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(command.Email.Trim());
        if (user is null)
        {
            users.PasswordHasher.VerifyHashedPassword(new User(), s_dummyHash.Value, command.Password);
            throw new UnauthorizedException(InvalidCredentials);
        }

        var result = await signIn.CheckPasswordSignInAsync(user, command.Password, lockoutOnFailure: true);
        if (result.IsNotAllowed)
        {
            // o Identity barra a conta não confirmada antes de conferir a senha e o bloqueio: sem isto,
            // a senha errada responderia "não confirmado" (revela a conta e não conta a tentativa)
            await EnsureNotLockedAsync(user);
            if (!await users.CheckPasswordAsync(user, command.Password))
            {
                await users.AccessFailedAsync(user);
                await EnsureNotLockedAsync(user); // a quinta já responde 423, como no login normal
                throw new UnauthorizedException(InvalidCredentials);
            }
            throw new ForbiddenException("E-mail não confirmado",
                "confirme o e-mail pelo link que enviamos; para receber outro, use o reenvio");
        }
        if (result.IsLockedOut)
        {
            await EnsureNotLockedAsync(user);
        }
        if (!result.Succeeded)
        {
            throw new UnauthorizedException(InvalidCredentials);
        }

        return await AfterFirstFactorAsync(user, ct);
    }

    /// <summary>
    /// O primeiro fator (senha ou Google) conferiu: a sessão, se a conta não precisa do segundo; senão, o
    /// tíquete do código (2FA ativa) ou o do cadastro do autenticador (operador sem 2FA).
    /// </summary>
    public async Task<SignInOutcome> AfterFirstFactorAsync(User user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.TwoFactorEnabled)
        {
            return SignInOutcome.Of(await mfaTickets.IssueAsync(user, MfaTicketPurpose.Login, ct));
        }
        if (await users.IsInRoleAsync(user, Roles.Admin))
        {
            return SignInOutcome.Of(await mfaTickets.IssueAsync(user, MfaTicketPurpose.Setup, ct));
        }
        return SignInOutcome.Of(await StartAsync(user, ct));
    }

    /// <summary>
    /// Abre uma sessão nova (uma família de refresh tokens) para quem já foi autenticado. Sem
    /// <paramref name="mfa"/>, recusa a conta que precisa do segundo fator: nenhum caminho novo abre sessão de
    /// operador só com a senha por esquecimento.
    /// </summary>
    public async Task<IssuedSession> StartAsync(User user, CancellationToken ct, bool mfa = false)
    {
        ArgumentNullException.ThrowIfNull(user);
        IReadOnlyList<string> roles = [.. await users.GetRolesAsync(user)];
        if (!mfa && RequiresMfa(user, roles))
        {
            throw new InvalidOperationException("sessão de conta com segundo fator obrigatório sem o segundo fator");
        }
        var refreshToken = RefreshTokenValue.New();
        refreshTokens.Add(RefreshToken.Issue(user.Id, user.SecurityStamp, RefreshTokenValue.Hash(refreshToken), clock.GetUtcNow(), options.Value.RefreshTokenLifetime, mfa));
        await refreshTokens.SaveChangesAsync(ct);
        return await SessionAsync(user, roles, refreshToken, mfa, ct);
    }

    /// <summary>A conta só entra com o segundo fator: 2FA ativa, ou o papel de operador.</summary>
    public static bool RequiresMfa(User user, IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.TwoFactorEnabled || roles.Contains(Roles.Admin, StringComparer.Ordinal);
    }

    public async Task<IssuedSession> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Hash(refreshToken);
        var family = hash is null ? null : await refreshTokens.FamilyOfAsync(hash, ct);
        if (family is null)
        {
            throw new UnauthorizedException(InvalidRefreshToken);
        }

        // sob o lock da família: um segundo refresh com o mesmo token espera este terminar e cai no reuso
        var session = await transaction.RunAsync(async () =>
        {
            await refreshTokens.LockFamilyAsync(family.Value, ct);
            var current = await refreshTokens.FindByHashAsync(hash!, ct);
            var now = clock.GetUtcNow();
            if (current is null)
            {
                return null;
            }
            if (current.WasRotated)
            {
                // trocado há segundos, com a sessão aberta: a outra metade de duas requisições paralelas
                var reuseInterval = options.Value.RefreshReuseInterval;
                if (current.RotatedWithin(now, reuseInterval) && await refreshTokens.FamilyIsActiveAsync(current.FamilyId, now, ct)
                    && await users.FindByIdAsync(current.UserId.ToString()) is { } owner)
                {
                    if (!current.MatchesStamp(owner.SecurityStamp))
                    {
                        await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, RevocationReason.StampChanged, ct);
                        return null;
                    }
                    IReadOnlyList<string> ownerRoles = [.. await users.GetRolesAsync(owner)];
                    if (!current.Mfa && RequiresMfa(owner, ownerRoles))
                    {
                        await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, RevocationReason.MfaRequired, ct);
                        return null;
                    }
                    var siblingToken = RefreshTokenValue.New();
                    refreshTokens.Add(current.Sibling(RefreshTokenValue.Hash(siblingToken), now, options.Value.RefreshTokenLifetime, reuseInterval));
                    await refreshTokens.SaveChangesAsync(ct);
                    return await SessionAsync(owner, ownerRoles, siblingToken, current.Mfa, ct);
                }
                // fora do intervalo: alguém tem uma cópia; nenhum token da sessão vale mais
                await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, RevocationReason.Reuse, ct);
                return null;
            }
            // o bloqueio por senha errada não derruba sessões: senão bastariam 5 tentativas com o e-mail
            // de alguém para desconectá-lo
            var user = current.IsActive(now) ? await users.FindByIdAsync(current.UserId.ToString()) : null;
            if (user is null)
            {
                return null;
            }
            // a senha mudou depois do login. A troca de senha revoga as sessões sem o lock das famílias:
            // um refresh no meio dela cria um token que o UPDATE da troca não vê (READ COMMITTED). O
            // carimbo pega esse token aqui, no próximo refresh, e a família toda cai
            if (!current.MatchesStamp(user.SecurityStamp))
            {
                await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, RevocationReason.StampChanged, ct);
                return null;
            }
            // sessão sem o segundo fator de uma conta que agora o exige (aberta antes do 2FA, ou de alguém que
            // virou operador depois do login): não renova; a pessoa entra de novo, com o código
            IReadOnlyList<string> roles = [.. await users.GetRolesAsync(user)];
            if (!current.Mfa && RequiresMfa(user, roles))
            {
                await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, RevocationReason.MfaRequired, ct);
                return null;
            }

            var nextToken = RefreshTokenValue.New();
            var next = current.Rotate(RefreshTokenValue.Hash(nextToken), now, options.Value.RefreshTokenLifetime);
            refreshTokens.Add(next);
            await refreshTokens.SaveChangesAsync(ct);
            // irmãos do intervalo de reuso que ficaram para trás (a resposta que o navegador não guardou)
            // saem: a sessão volta a ter um token só
            await refreshTokens.RevokeFamilyAsync(current.FamilyId, now, RevocationReason.Rotated, ct, except: next.Id);
            return await SessionAsync(user, roles, nextToken, current.Mfa, ct);
        }, ct);
        return session ?? throw new UnauthorizedException(InvalidRefreshToken);
    }

    /// <summary>Logout: derruba a sessão do refresh token. Token desconhecido não é erro (idempotente).</summary>
    public async Task RevokeAsync(string refreshToken, CancellationToken ct)
    {
        if (Hash(refreshToken) is { } hash && await refreshTokens.FamilyOfAsync(hash, ct) is { } family)
        {
            await transaction.RunAsync(async () =>
            {
                await refreshTokens.LockFamilyAsync(family, ct); // espera um refresh em curso: o token que ele criou cai junto
                await refreshTokens.RevokeFamilyAsync(family, clock.GetUtcNow(), RevocationReason.Logout, ct);
                return true;
            }, ct);
        }
    }

    /// <exception cref="AccountLockedException">A conta está bloqueada por tentativas erradas.</exception>
    public async Task EnsureNotLockedAsync(User user)
    {
        if (!await users.IsLockedOutAsync(user))
        {
            return;
        }
        var end = await users.GetLockoutEndDateAsync(user);
        var retryAfter = end is { } e && e > clock.GetUtcNow() ? e - clock.GetUtcNow() : TimeSpan.FromMinutes(1);
        throw new AccountLockedException(retryAfter);
    }

    private static byte[]? Hash(string refreshToken) =>
        string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > RefreshTokenValue.MaxLength
            ? null
            : RefreshTokenValue.Hash(refreshToken);

    private async Task<IssuedSession> SessionAsync(User user, IReadOnlyList<string> roles, string refreshToken, bool mfa, CancellationToken ct)
    {
        var accessToken = await accessTokens.IssueAsync(user, roles, ct, mfa);
        return new IssuedSession(accessToken, options.Value.AccessTokenLifetime, refreshToken, options.Value.RefreshTokenLifetime);
    }
}
