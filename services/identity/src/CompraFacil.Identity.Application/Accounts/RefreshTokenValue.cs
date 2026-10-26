using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace CompraFacil.Identity.Application.Accounts;

/// <summary>O refresh token em si: 256 bits aleatórios em base64url. O banco guarda só o SHA-256.</summary>
public static class RefreshTokenValue
{
    public const int MaxLength = 128;

    public static string New() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
