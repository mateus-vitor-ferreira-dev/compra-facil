using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;

namespace Embrulhado.Identity.Application.Accounts;

/// <summary>
/// TOTP (RFC 6238) no formato que todo app autenticador entende: HMAC-SHA1, 6 dígitos, passos de 30 s,
/// chave em Base32 (RFC 4648). O HMAC-SHA1 é o padrão dos apps; como HMAC, continua seguro (a colisão do
/// SHA-1 não ajuda a forjar um HMAC).
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;

    /// <summary>Quem emite, no app autenticador e no URI <c>otpauth://</c>.</summary>
    public const string Issuer = "Embrulhado";

    /// <summary>Onde fica (user_tokens) o último passo aceito: o mesmo código não vale duas vezes.</summary>
    public const string LastStepProvider = "[EmbrulhadoTotp]";

    public const string LastStepName = "LastStep";

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>O passo de tempo de <paramref name="now"/> (segundos Unix / 30).</summary>
    public static long StepAt(DateTimeOffset now) => now.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>O código de 6 dígitos da chave no passo <paramref name="step"/>.</summary>
    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "RFC 6238: os apps autenticadores usam HMAC-SHA1, que segue seguro como HMAC")]
    public static string Code(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(key, counter, hash);
        var offset = hash[^1] & 0x0f; // truncamento dinâmico (RFC 4226, 5.3)
        var binary = (BinaryPrimitives.ReadInt32BigEndian(hash[offset..]) & 0x7fffffff) % 1_000_000;
        return binary.ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>Tira espaços e hífens que a pessoa digitou ("123 456"); nulo se não sobrarem 6 dígitos.</summary>
    public static string? Normalize(string? code)
    {
        if (code is null)
        {
            return null;
        }
        var digits = new string([.. code.Where(c => c is not (' ' or '-'))]);
        return digits.Length == Digits && digits.All(char.IsAsciiDigit) ? digits : null;
    }

    /// <summary>O URI do QR code (formato Key Uri do Google Authenticator), com o emissor no rótulo e no parâmetro.</summary>
    public static string AuthenticatorUri(string account, string base32Key) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={base32Key}&issuer={Uri.EscapeDataString(Issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";

    /// <summary>A chave do Identity (Base32, sem preenchimento) em bytes; ignora espaços e caixa.</summary>
    public static byte[] FromBase32(string base32)
    {
        ArgumentNullException.ThrowIfNull(base32);
        var clean = base32.Replace(" ", "", StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var output = new byte[clean.Length * 5 / 8];
        int buffer = 0, bits = 0, index = 0;
        foreach (var c in clean)
        {
            var value = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("chave fora do alfabeto Base32");
            }
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output[index++] = (byte)(buffer >> (bits - 8));
                bits -= 8;
            }
        }
        return output;
    }

    /// <summary>A chave em grupos de 4 letras, para digitar no app sem ler o QR code.</summary>
    public static string Grouped(string base32Key)
    {
        ArgumentNullException.ThrowIfNull(base32Key);
        return string.Join(' ', base32Key.ToLowerInvariant().Chunk(4).Select(c => new string(c)));
    }
}
