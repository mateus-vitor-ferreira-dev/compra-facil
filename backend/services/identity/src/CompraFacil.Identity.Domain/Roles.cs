using Microsoft.AspNetCore.Identity;

namespace CompraFacil.Identity.Domain;

/// <summary>Papel de uma conta; vai no claim <c>roles</c> do JWT.</summary>
public sealed class Role : IdentityRole<Guid>;

/// <summary>Os dois papéis da loja. Os ids são fixos: entram pela migration (HasData).</summary>
public static class Roles
{
    /// <summary>Quem compra: toda conta criada pelo cadastro.</summary>
    public const string Customer = "customer";

    /// <summary>Operador: rotas /v1/admin/* dos serviços e o painel administrativo.</summary>
    public const string Admin = "admin";

    public static readonly Guid CustomerId = Guid.Parse("0199f6a0-0000-7000-8000-00000000c057");
    public static readonly Guid AdminId = Guid.Parse("0199f6a0-0000-7000-8000-00000000ad31");
}
