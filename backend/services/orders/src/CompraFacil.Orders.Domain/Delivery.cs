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

/// <summary>
/// O frete escolhido no checkout (#222): o serviço, o preço e o prazo em dias úteis como o fulfillment cotou, e
/// a data que a loja promete ao cliente (o fim do prazo, contado do pedido).
/// </summary>
public sealed record OrderShipping
{
    public OrderShipping(string service, string name, Money price, int minDays, int maxDays, DateOnly promisedBy)
    {
        (Service, Name, Price, MinDays, MaxDays, PromisedBy) = (service, name, price, minDays, maxDays, promisedBy);
    }

    private OrderShipping()
    {
        // EF Core: o Money aninhado não entra pelo construtor
    }

    public string Service { get; init; } = "";
    public string Name { get; init; } = "";
    public Money Price { get; init; }
    public int MinDays { get; init; }
    public int MaxDays { get; init; }
    public DateOnly PromisedBy { get; init; }

    // Brasília: UTC-3 o ano todo desde o fim do horário de verão (2019). Fixo, porque a imagem do serviço
    // (chiseled) não traz a base de fusos (tzdata)
    private static readonly TimeSpan s_brasilia = TimeSpan.FromHours(-3);

    /// <summary>A data prometida: o dia do pedido em Brasília mais o prazo máximo, só em dias úteis (sem feriados).</summary>
    public static DateOnly Promise(DateTimeOffset placedAt, int businessDays)
    {
        var day = DateOnly.FromDateTime(placedAt.ToOffset(s_brasilia).DateTime);
        for (var left = businessDays; left > 0;)
        {
            day = day.AddDays(1);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                left--;
            }
        }
        return day;
    }
}
