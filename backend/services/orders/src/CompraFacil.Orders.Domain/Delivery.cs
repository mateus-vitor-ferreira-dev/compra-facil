namespace CompraFacil.Orders.Domain;

/// <summary>Quem recebe o pedido: o nome e o CPF da nota fiscal (#221) e o telefone que a transportadora usa.</summary>
public sealed record Recipient(string Name, string Cpf, string Phone);

/// <summary>Onde entregar: o endereço que vai na nota e ao fornecedor. CEP só com os 8 dígitos; UF com 2 letras.</summary>
public sealed record ShippingAddress(
    string ZipCode, string Street, string Number, string? Complement, string District, string City, string State);

/// <summary>As regras brasileiras dos dados de entrega: CPF com dígitos verificadores, CEP e UF.</summary>
public static class BrazilianDocuments
{
    public static readonly IReadOnlySet<string> States = new HashSet<string>(StringComparer.Ordinal)
    {
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA",
        "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO",
    };

    public static string DigitsOf(string? value) => new([.. (value ?? "").Where(char.IsAsciiDigit)]);

    /// <summary>CPF de 11 dígitos com os dois verificadores certos (e não todos iguais, como 111.111.111-11).</summary>
    public static bool IsValidCpf(string? value)
    {
        var d = DigitsOf(value);
        if (d.Length != 11 || d.Distinct().Count() == 1)
        {
            return false;
        }
        return Check(d, 9) == d[9] - '0' && Check(d, 10) == d[10] - '0';

        static int Check(string digits, int length)
        {
            var sum = 0;
            for (var i = 0; i < length; i++)
            {
                sum += (digits[i] - '0') * (length + 1 - i);
            }
            var rest = sum * 10 % 11;
            return rest == 10 ? 0 : rest;
        }
    }

    public static bool IsValidZipCode(string? value) => value is { Length: 8 } && value.All(char.IsAsciiDigit);
}
