using System.Text.RegularExpressions;

namespace Embrulhado.Identity.Domain;

/// <summary>
/// O nome que o cliente escolhe. Ele vai na saudação dos e-mails da loja, e o cadastro e a compra sem conta
/// aceitam o e-mail de outra pessoa: um nome como "evil.com/verificar" viraria um link clicável num e-mail
/// legítimo, assinado pela loja. Por isso só letras (com acento), espaços, apóstrofo, hífen e ponto, sem
/// nada com cara de endereço.
/// </summary>
public static partial class PersonName
{
    public const string InvalidMessage = "use só letras, espaços, apóstrofo, hífen e ponto";

    /// <summary>Quando o nome que veio de fora (o Google) não serve, nem o do e-mail.</summary>
    public const string Fallback = "Cliente";

    /// <summary>Sem espaços nas pontas e com um só entre as palavras.</summary>
    public static string Normalize(string? name) => Spaces().Replace(name ?? "", " ").Trim();

    /// <summary>O nome, já normalizado, é aceito: tamanho, caracteres e nada de link, e-mail ou domínio.</summary>
    public static bool IsValid(string? name)
    {
        var normalized = Normalize(name);
        return normalized.Length is > 0 and <= User.MaxNameLength
            && Allowed().IsMatch(normalized)
            && !DomainLike().IsMatch(normalized);
    }

    /// <summary>
    /// O nome de quem entra com o Google: o login não pode falhar por causa dele, então o que não passa
    /// vira o começo do e-mail ("joao.silva" → "joao silva") ou, se nem isso servir, <see cref="Fallback"/>.
    /// </summary>
    public static string Sanitize(string? name, string email)
    {
        if (IsValid(name))
        {
            return Normalize(name);
        }
        var local = (email ?? "").Split('@')[0];
        var fromEmail = Normalize(NotALetter().Replace(local, " "));
        return IsValid(fromEmail) ? fromEmail : Fallback;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // letras de qualquer alfabeto (e acentos combinados), espaço, apóstrofo (reto ou curvo), hífen e ponto;
    // sem dígitos, '/', ':' ou '@'. Pelo menos uma letra.
    [GeneratedRegex(@"^(?=.*\p{L})[\p{L}\p{M} '’.\-]+$")]
    private static partial Regex Allowed();

    // cara de domínio: "evil.com" (letra, ponto e 2+ letras) ou "www."; iniciais como "J. R." passam
    [GeneratedRegex(@"\p{L}\.\p{L}{2,}|(^|\s)www\.", RegexOptions.IgnoreCase)]
    private static partial Regex DomainLike();

    [GeneratedRegex(@"[^\p{L}\p{M}]+")]
    private static partial Regex NotALetter();
}
