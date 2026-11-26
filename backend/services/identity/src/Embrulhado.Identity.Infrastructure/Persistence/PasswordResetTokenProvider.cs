using Embrulhado.Identity.Application.Accounts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Embrulhado.Identity.Infrastructure.Persistence;

/// <summary>
/// O token do link de "esqueci minha senha": o mesmo formato do <see cref="DataProtectorTokenProvider{TUser}"/>
/// (criação, conta, finalidade e carimbo de segurança, cifrados pelo Data Protection), com validade própria
/// (Identity:PasswordReset:TokenLifetime, 1 hora) e o relógio do serviço. O do Identity usa a validade global,
/// a do link de confirmação (1 dia), e o relógio da máquina. Trocar a senha muda o carimbo: o token vale
/// uma vez só.
/// </summary>
internal sealed class PasswordResetTokenProvider<TUser>(
    IDataProtectionProvider dataProtection, IOptions<PasswordResetOptions> options, TimeProvider clock) : IUserTwoFactorTokenProvider<TUser>
    where TUser : class
{
    // finalidade própria: um token do link de confirmação não abre esta porta, nem o contrário
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Embrulhado.Identity.PasswordReset");

    public async Task<string> GenerateAsync(string purpose, UserManager<TUser> manager, TUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(clock.GetUtcNow().UtcTicks);
            writer.Write(await manager.GetUserIdAsync(user));
            writer.Write(purpose ?? "");
            writer.Write(await StampAsync(manager, user));
        }
        return Convert.ToBase64String(_protector.Protect(stream.ToArray()));
    }

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<TUser> manager, TUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        try
        {
            using var stream = new MemoryStream(_protector.Unprotect(Convert.FromBase64String(token)));
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);
            var createdAt = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
            if (createdAt + options.Value.TokenLifetime < clock.GetUtcNow())
            {
                return false;
            }
            return reader.ReadString() == await manager.GetUserIdAsync(user)
                && reader.ReadString() == purpose
                && reader.ReadString() == await StampAsync(manager, user)
                && stream.Position == stream.Length;
        }
        catch (Exception e) when (e is FormatException or System.Security.Cryptography.CryptographicException or EndOfStreamException or ArgumentOutOfRangeException)
        {
            return false; // adulterado, de outra chave ou truncado: só não vale
        }
    }

    public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<TUser> manager, TUser user) => Task.FromResult(false);

    private static async Task<string> StampAsync(UserManager<TUser> manager, TUser user) =>
        manager.SupportsUserSecurityStamp ? await manager.GetSecurityStampAsync(user) ?? "" : "";
}
