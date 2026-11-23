using System.Text;

namespace CompraFacil.Orders.Domain;

/// <summary>
/// As regras do texto que o cliente escreve na compra (ex.: o nome na estampa), as mesmas do catalog: normalizado
/// (sem espaços nas pontas nem repetidos), até o máximo do produto, só letras do alfabeto latino (com acento),
/// algarismos, espaço e <c>. , ' - &amp; ! ? /</c>. Valem quando o orders confere o pedido pela cópia local, com o
/// catalog fora.
/// </summary>
public static class Personalization
{
    /// <summary>O máximo que um produto pode pedir (o max_length do catalog vai até aqui).</summary>
    public const int MaxLength = 40;

    private const string Punctuation = ".,'-&!?/";

    /// <summary>O texto sem espaços nas pontas e com os de dentro juntos num só; vazio vira nulo.</summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Se o texto (já normalizado) vale para o produto: sem personalização (maxLength nulo), só o vazio; com ela,
    /// obrigatório quando required, até maxLength caracteres e só com o que a arte imprime.
    /// </summary>
    public static bool Accepts(string? text, int? maxLength, bool required)
    {
        if (maxLength is not { } max)
        {
            return text is null;
        }
        if (text is null)
        {
            return !required;
        }
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Printable(rune))
            {
                return false;
            }
            count++;
        }
        return count <= max;
    }

    private static bool Printable(Rune r) =>
        r.Value == ' '
        || (r.IsBmp && Punctuation.Contains((char)r.Value, StringComparison.Ordinal))
        || r.Value is >= '0' and <= '9'
        || (Rune.IsLetter(r) && IsLatin(r.Value));

    // os blocos do alfabeto latino (com os acentos do português e das outras línguas que o usam)
    private static bool IsLatin(int c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= 0x00C0 and <= 0x024F) or (>= 0x1E00 and <= 0x1EFF);
}
