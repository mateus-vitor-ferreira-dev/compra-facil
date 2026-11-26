using Embrulhado.BuildingBlocks.Web;
using Embrulhado.Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace Embrulhado.Identity.Application.Accounts;

public interface IMfaTicketRepository
{
    void Add(MfaTicket ticket);

    Task<MfaTicket?> FindByHashAsync(byte[] tokenHash, CancellationToken ct);

    /// <summary>
    /// Gasta o tíquete direto no banco, só se ninguém o gastou antes (UPDATE ... WHERE consumed_at IS NULL):
    /// de duas requisições com o mesmo tíquete, só uma passa.
    /// </summary>
    Task<bool> TryConsumeAsync(Guid ticketId, DateTimeOffset now, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>O primeiro fator conferiu, mas a sessão depende do segundo: o tíquete e para que ele serve.</summary>
public sealed record MfaChallenge(string Status, string Ticket, TimeSpan ExpiresIn)
{
    /// <summary>A conta tem 2FA: falta o código do autenticador (ou um de recuperação).</summary>
    public const string Required = "mfa_required";

    /// <summary>Operador sem 2FA: primeiro cadastra o autenticador; o tíquete não abre sessão sozinho.</summary>
    public const string SetupRequired = "mfa_setup_required";
}

/// <summary>O que o login (senha ou Google) devolve: a sessão, ou o desafio do segundo fator.</summary>
public sealed record SignInOutcome(IssuedSession? Session, MfaChallenge? Challenge)
{
    public static SignInOutcome Of(IssuedSession session) => new(session, null);

    public static SignInOutcome Of(MfaChallenge challenge) => new(null, challenge);
}

/// <summary>
/// Emissão e conferência dos tíquetes do segundo fator (<see cref="MfaTicket"/>). Tíquete desconhecido,
/// vencido, gasto, de outra finalidade ou de antes da última troca de senha dão o mesmo 401.
/// </summary>
public sealed class MfaTickets(IMfaTicketRepository tickets, UserManager<User> users, TimeProvider clock)
{
    public const string InvalidTicket = "a etapa de verificação venceu ou já foi usada; entre de novo com a senha";

    public async Task<MfaChallenge> IssueAsync(User user, MfaTicketPurpose purpose, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        var value = RefreshTokenValue.New();
        tickets.Add(MfaTicket.Issue(user.Id, purpose, user.SecurityStamp, RefreshTokenValue.Hash(value), clock.GetUtcNow()));
        await tickets.SaveChangesAsync(ct);
        return new MfaChallenge(
            purpose == MfaTicketPurpose.Setup ? MfaChallenge.SetupRequired : MfaChallenge.Required,
            value, MfaTicket.LifetimeOf(purpose));
    }

    /// <summary>O tíquete ainda usável para <paramref name="purpose"/> e a conta dele.</summary>
    /// <exception cref="UnauthorizedException">Tíquete que não serve mais.</exception>
    public async Task<(MfaTicket Ticket, User User)> FindAsync(string ticket, MfaTicketPurpose purpose, CancellationToken ct)
    {
        var found = string.IsNullOrWhiteSpace(ticket) || ticket.Length > RefreshTokenValue.MaxLength
            ? null
            : await tickets.FindByHashAsync(RefreshTokenValue.Hash(ticket), ct);
        var user = found is null ? null : await users.FindByIdAsync(found.UserId.ToString());
        if (found is null || user is null || !found.IsUsable(purpose, user.SecurityStamp, clock.GetUtcNow()))
        {
            throw new UnauthorizedException(InvalidTicket);
        }
        return (found, user);
    }

    /// <summary>Gasta o tíquete; se outra requisição o gastou antes, 401.</summary>
    public async Task ConsumeAsync(MfaTicket ticket, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!await tickets.TryConsumeAsync(ticket.Id, clock.GetUtcNow(), ct))
        {
            throw new UnauthorizedException(InvalidTicket);
        }
    }
}
