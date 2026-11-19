using System.Security.Cryptography;
using System.Text;

namespace CompraFacil.Identity.Application.Accounts;

/// <summary>
/// Códigos de recuperação do segundo fator: 10 de uso único, cada um com 50 bits aleatórios (10 caracteres do
/// Base32 de Crockford, sem i, l, o e u, que se confundem). O ASP.NET Core Identity guarda os códigos em texto
/// puro na user_tokens; aqui ele recebe só o SHA-256 de cada um, e o banco vazado não entra na conta. Hash
/// rápido basta: o código é aleatório, não uma senha escolhida por alguém.
/// </summary>
public static class RecoveryCodes
{
    public const int Count = 10;
    private const int Length = 10;
    private const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    /// <summary>Códigos novos, como a pessoa vai vê-los ("k7m2p-9xq4r").</summary>
    public static IReadOnlyList<string> New() =>
        [.. Enumerable.Range(0, Count).Select(_ =>
        {
            var chars = RandomNumberGenerator.GetItems<char>(Alphabet, Length);
            return $"{new string(chars, 0, Length / 2)}-{new string(chars, Length / 2, Length / 2)}";
        })];

    /// <summary>O que vai para o banco: o SHA-256 (hex) do código sem hífen, espaço nem caixa.</summary>
    public static string Hash(string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code))));

    /// <summary>Tem o formato de um código (10 caracteres do alfabeto, com ou sem hífen e espaços).</summary>
    public static bool LooksValid(string? code) =>
        code is not null && code.Length <= 32 && Normalize(code) is { Length: Length } n && n.All(Alphabet.Contains);

    private static string Normalize(string code) =>
        new([.. code.Where(c => c is not ('-' or ' ')).Select(char.ToLowerInvariant)]);
}
