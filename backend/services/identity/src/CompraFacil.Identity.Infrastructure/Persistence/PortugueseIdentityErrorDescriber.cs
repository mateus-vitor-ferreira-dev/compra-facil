using Microsoft.AspNetCore.Identity;

namespace CompraFacil.Identity.Infrastructure.Persistence;

/// <summary>Mensagens do Identity que chegam ao cliente (422 do cadastro), em português como o resto da API.</summary>
internal sealed class PortugueseIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"precisa ter pelo menos {length} caracteres" };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"precisa ter pelo menos {uniqueChars} caracteres diferentes" };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = "e-mail inválido" };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = "e-mail com caracteres não permitidos" };

    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = "já existe uma conta com este e-mail" };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = "já existe uma conta com este e-mail" };
}
