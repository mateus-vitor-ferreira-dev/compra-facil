using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace CompraFacil.Identity.Application.Accounts;

public sealed record AccountView(
    Guid Id, string Email, string Name, bool EmailConfirmed, IReadOnlyList<string> Roles, DateTimeOffset CreatedAt, bool IsGuest = false,
    bool TwoFactorEnabled = false, int? RecoveryCodesLeft = null)
{
    public static AccountView From(User user, IEnumerable<string> roles, int? recoveryCodesLeft = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new(user.Id, user.ContactEmail, user.Name, user.EmailConfirmed, [.. roles.Order(StringComparer.Ordinal)], user.CreatedAt, user.IsGuest,
            user.TwoFactorEnabled, user.TwoFactorEnabled ? recoveryCodesLeft : null);
    }
}

public sealed class AccountQueries(UserManager<User> users)
{
    public async Task<AccountView> GetAsync(Guid userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("conta não encontrada");
        int? left = user.TwoFactorEnabled ? await users.CountRecoveryCodesAsync(user) : null;
        return AccountView.From(user, await users.GetRolesAsync(user), left);
    }
}
