using System.Globalization;
using System.Text;
using Embrulhado.Fulfillment.Domain;

namespace Embrulhado.Fulfillment.Application.ManualSupplier;

/// <summary>Uma linha da planilha com problema: o número dela (o cabeçalho é a 1) e o que está errado.</summary>
public sealed record SheetError(int Line, string Message);

/// <summary>A planilha lida: os itens, se nenhuma linha teve erro, ou os erros (nada é importado pela metade).</summary>
public sealed record ParsedSheet(IReadOnlyList<ManualCatalogItem> Items, IReadOnlyList<SheetError> Errors)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// A planilha do fornecedor manual (#412), em CSV, como a equipe exporta do Excel ou do Google Planilhas: separada
/// por ponto e vírgula (o padrão do Excel em português) ou vírgula, com aspas quando o texto tem o separador. O
/// cabeçalho tem os nomes das colunas, em qualquer ordem:
/// <c>sku</c>, <c>nome</c>, <c>categoria</c>, <c>custo</c> e <c>estoque</c> obrigatórias; <c>peso_g</c> e <c>foto</c>
/// opcionais. O custo aceita "12,90", "12.90", "R$ 1.234,56". UTF-8 (com ou sem BOM) ou, se não for, Latin-1 (o
/// "CSV" antigo do Excel).
/// </summary>
public static class ManualCatalogSheet
{
    public const int MaxRows = 5000;
    public const int MaxBytes = 5 * 1024 * 1024;

    private static readonly string[] s_required = ["sku", "nome", "categoria", "custo", "estoque"];
    private static readonly string[] s_known = [.. s_required, "peso_g", "foto"];

    /// <summary>O modelo que o painel oferece para baixar.</summary>
    public const string Template = "sku;nome;categoria;custo;estoque;peso_g;foto\r\n"
        + "FON-BT-01;Fone de ouvido Bluetooth;fones-over-ear;89,90;25;250;https://exemplo.com/fone.jpg\r\n";

    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    public static ParsedSheet Parse(string text, DateTimeOffset importedAt)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = Rows(text).ToList();
        while (rows.Count > 0 && rows[^1].Fields.All(string.IsNullOrWhiteSpace))
        {
            rows.RemoveAt(rows.Count - 1);
        }
        if (rows.Count == 0)
        {
            return new ParsedSheet([], [new SheetError(1, "a planilha está vazia")]);
        }
        var header = rows[0].Fields.Select(h => h.Trim().ToLowerInvariant()).ToList();
        var missing = s_required.Where(c => !header.Contains(c)).ToList();
        if (missing.Count > 0)
        {
            return new ParsedSheet([], [new SheetError(1, $"faltam as colunas {string.Join(", ", missing)} no cabeçalho")]);
        }
        var unknown = header.Where(h => h.Length > 0 && !s_known.Contains(h)).ToList();
        if (unknown.Count > 0)
        {
            return new ParsedSheet([], [new SheetError(1, $"colunas que a loja não conhece: {string.Join(", ", unknown)}")]);
        }
        if (rows.Count - 1 > MaxRows)
        {
            return new ParsedSheet([], [new SheetError(1, $"a planilha tem {rows.Count - 1} produtos; o máximo é {MaxRows}")]);
        }
        int Col(string name) => header.IndexOf(name);
        var errors = new List<SheetError>();
        var items = new List<ManualCatalogItem>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows.Skip(1))
        {
            if (row.Fields.All(string.IsNullOrWhiteSpace))
            {
                continue; // linha em branco no meio
            }
            string Field(string name) => Col(name) is var i and >= 0 && i < row.Fields.Count ? row.Fields[i].Trim() : "";
            var before = errors.Count;
            void Fail(string message) => errors.Add(new SheetError(row.Line, message));

            var sku = Field("sku").ToUpperInvariant();
            if (sku.Length is 0 or > 64 || !sku.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            {
                Fail("sku vazio ou com caracteres fora de letras, números, - _ e . (até 64)");
            }
            else if (seen.TryGetValue(sku, out var first))
            {
                Fail($"sku {sku} repetido (já está na linha {first})");
            }
            else
            {
                seen[sku] = row.Line;
            }
            var name = Field("nome");
            if (name.Length is 0 or > 200)
            {
                Fail("nome vazio ou com mais de 200 caracteres");
            }
            var category = Field("categoria");
            if (category.Length is 0 or > 100)
            {
                Fail("categoria vazia ou com mais de 100 caracteres");
            }
            var cost = Money(Field("custo"));
            if (cost is not > 0)
            {
                Fail($"custo inválido: \"{Field("custo")}\" (ex.: 12,90)");
            }
            var stockText = Field("estoque");
            if (!int.TryParse(stockText, NumberStyles.None, CultureInfo.InvariantCulture, out var stock))
            {
                Fail($"estoque inválido: \"{stockText}\" (um número inteiro, 0 se acabou)");
            }
            int? weight = null;
            if (Field("peso_g") is { Length: > 0 } weightText)
            {
                if (int.TryParse(weightText, NumberStyles.None, CultureInfo.InvariantCulture, out var w) && w is > 0 and <= 100_000)
                {
                    weight = w;
                }
                else
                {
                    Fail($"peso_g inválido: \"{weightText}\" (gramas, de 1 a 100000)");
                }
            }
            string? image = null;
            if (Field("foto") is { Length: > 0 } imageText)
            {
                if (Uri.TryCreate(imageText, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && imageText.Length <= 500)
                {
                    image = uri.ToString();
                }
                else
                {
                    Fail("foto precisa ser um endereço https://");
                }
            }
            if (errors.Count == before)
            {
                items.Add(ManualCatalogItem.Create(sku, name, category, cost!.Value, stock, weight, image, importedAt));
            }
        }
        if (errors.Count == 0 && items.Count == 0)
        {
            errors.Add(new SheetError(2, "a planilha não tem nenhum produto"));
        }
        return errors.Count == 0 ? new ParsedSheet(items, []) : new ParsedSheet([], errors);
    }

    /// <summary>Centavos de "12,90", "12.90", "1.234,56", "1,234.56" ou "R$ 12,90"; nulo se não for um valor.</summary>
    public static long? Money(string text)
    {
        var t = text.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        if (t.Length == 0 || !t.All(c => char.IsAsciiDigit(c) || c is ',' or '.'))
        {
            return null;
        }
        // o último separador é o decimal, se tiver 1 ou 2 dígitos depois; os outros são de milhar
        var last = t.LastIndexOfAny([',', '.']);
        string whole, fraction;
        if (last >= 0 && t.Length - last - 1 is 1 or 2)
        {
            (whole, fraction) = (t[..last], t[(last + 1)..].PadRight(2, '0'));
        }
        else
        {
            (whole, fraction) = (t, "00");
        }
        whole = whole.Replace(",", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal);
        return whole.Length is > 0 and <= 9 && long.TryParse(whole + fraction, NumberStyles.None, CultureInfo.InvariantCulture, out var cents)
            ? cents
            : null;
    }

    private sealed record Row(int Line, List<string> Fields);

    /// <summary>As linhas do CSV, com o separador do cabeçalho e aspas (RFC 4180): "" dentro de aspas é uma aspa.</summary>
    private static IEnumerable<Row> Rows(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        var separator = firstLine.Count(c => c == ';') >= firstLine.Count(c => c == ',') ? ';' : ',';
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var line = 1;
        var start = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }
                    field.Append(c);
                }
                continue;
            }
            if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == separator)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                fields.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                yield return new Row(start, fields);
                fields = [];
                line++;
                start = line;
            }
            else
            {
                field.Append(c);
            }
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString().TrimEnd('\r'));
            yield return new Row(start, fields);
        }
    }
}
