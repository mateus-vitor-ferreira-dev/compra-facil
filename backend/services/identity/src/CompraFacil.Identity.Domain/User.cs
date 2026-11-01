using Microsoft.AspNetCore.Identity;

namespace CompraFacil.Identity.Domain;

/// <summary>
/// Conta de cliente ou operador. O e-mail é também o login (UserName); hash da senha, bloqueio por
/// tentativas e carimbo de segurança vêm do ASP.NET Core Identity.
/// </summary>
public sealed class User : IdentityUser<Guid>
{
    public const int MaxNameLength = 120;
    public const int MaxEmailLength = 254;

    public string Name { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>O e-mail de quem comprou sem conta. Fica fora do login: quem o digita não entra nesta conta.</summary>
    public string? GuestEmail { get; private set; }

    /// <summary>O <see cref="GuestEmail"/> normalizado, para achar as compras no reenvio do link.</summary>
    public string? NormalizedGuestEmail { get; private set; }

    /// <summary>A chave do formulário do checkout: o reenvio do mesmo formulário acha o mesmo convidado.</summary>
    public Guid? GuestCheckoutKey { get; private set; }

    public bool IsGuest => GuestEmail is not null;

    /// <summary>Para onde vão os avisos: nos convidados, o e-mail do campo <c>Email</c> é só um marcador.</summary>
    public string ContactEmail => GuestEmail ?? Email!;

    public static User Create(string email, string name, DateTimeOffset now)
    {
        var normalized = email.Trim();
        return new User
        {
            Id = Guid.CreateVersion7(),
            UserName = normalized,
            Email = normalized,
            Name = name.Trim(),
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Conta de quem compra sem cadastro: sem senha, com login e e-mail marcadores que ninguém digita. Cada
    /// compra tem a sua, e uma conta de verdade com o mesmo e-mail continua sendo outra conta.
    /// </summary>
    public static User CreateGuest(string email, string normalizedEmail, string name, Guid checkoutKey, DateTimeOffset now)
    {
        var id = Guid.CreateVersion7();
        return new User
        {
            Id = id,
            UserName = $"convidado-{id:N}",
            Email = $"{id:N}@convidado.invalid",
            EmailConfirmed = true, // o marcador; o e-mail digitado não foi verificado
            GuestEmail = email.Trim(),
            NormalizedGuestEmail = normalizedEmail,
            GuestCheckoutKey = checkoutKey,
            Name = name.Trim(),
            CreatedAt = now,
        };
    }
}
