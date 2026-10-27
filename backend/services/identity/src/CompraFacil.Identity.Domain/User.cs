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
}
