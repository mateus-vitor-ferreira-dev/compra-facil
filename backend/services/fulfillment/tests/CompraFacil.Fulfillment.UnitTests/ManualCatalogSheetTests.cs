using System.Text;
using CompraFacil.Fulfillment.Application.ManualSupplier;

namespace CompraFacil.Fulfillment.UnitTests;

/// <summary>A planilha do fornecedor manual (#412), como a equipe exporta do Excel ou do Google Planilhas.</summary>
public sealed class ManualCatalogSheetTests
{
    private static readonly DateTimeOffset s_now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("12,90", 1290L)]
    [InlineData("12.90", 1290L)]
    [InlineData("R$ 1.234,56", 123456L)]
    [InlineData("1,234.56", 123456L)]
    [InlineData("89", 8900L)]
    [InlineData("9,9", 990L)]
    [InlineData("1.234", 123400L)]
    [InlineData("doze", null)]
    [InlineData("", null)]
    [InlineData("-5", null)]
    public void Custo_aceita_o_jeito_brasileiro_e_o_americano(string text, long? cents) =>
        Assert.Equal(cents, ManualCatalogSheet.Money(text));

    [Fact]
    public void Le_ponto_e_virgula_com_aspas_e_colunas_em_qualquer_ordem()
    {
        var sheet = ManualCatalogSheet.Parse(
            "estoque;sku;nome;categoria;custo;foto\r\n"
            + "3;pan-22;\"Frigideira 22 cm; antiaderente, \"\"premium\"\"\";frigideiras;R$ 49,90;\r\n"
            + "\r\n"
            + "0;FON-1;Fone;fones-over-ear;10;https://img.exemplo/fone.jpg\r\n", s_now);

        Assert.True(sheet.Ok);
        Assert.Equal(2, sheet.Items.Count);
        var pan = sheet.Items[0];
        Assert.Equal(("PAN-22", "Frigideira 22 cm; antiaderente, \"premium\"", "frigideiras", 4990L, 3, (int?)null, (string?)null),
            (pan.Sku, pan.Name, pan.Category, pan.CostCents, pan.Stock, pan.WeightGrams, pan.ImageUrl));
        Assert.Equal(("https://img.exemplo/fone.jpg", 0), (sheet.Items[1].ImageUrl, sheet.Items[1].Stock));
        Assert.All(sheet.Items, i => Assert.Equal(s_now, i.ImportedAt));
    }

    [Fact]
    public void Le_virgula_quando_o_cabecalho_usa_virgula()
    {
        var sheet = ManualCatalogSheet.Parse("sku,nome,categoria,custo,estoque,peso_g\nA-1,Mouse,mouses,\"19,90\",5,120\n", s_now);
        Assert.True(sheet.Ok);
        Assert.Equal((1990L, (int?)120), (sheet.Items[0].CostCents, sheet.Items[0].WeightGrams));
    }

    [Fact]
    public void Cada_linha_com_erro_diz_o_numero_e_nada_entra()
    {
        var sheet = ManualCatalogSheet.Parse(
            "sku;nome;categoria;custo;estoque;peso_g;foto\n"
            + "A-1;Mouse;mouses;10;5;;\n"
            + "A-1;Mouse de novo;mouses;10;5;;\n"
            + "B 2;;mouses;0;x;0;http://inseguro\n", s_now);

        Assert.False(sheet.Ok);
        Assert.Empty(sheet.Items);
        Assert.Equal([3, 4, 4, 4, 4, 4, 4], sheet.Errors.Select(e => e.Line));
        Assert.Contains("repetido (já está na linha 2)", sheet.Errors[0].Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "vazia")]
    [InlineData("sku;nome;custo\n", "faltam as colunas categoria, estoque")]
    [InlineData("sku;nome;categoria;custo;estoque;preco\n", "não conhece: preco")]
    [InlineData("sku;nome;categoria;custo;estoque\n", "nenhum produto")]
    public void Cabecalho_ou_planilha_sem_produtos(string csv, string message)
    {
        var sheet = ManualCatalogSheet.Parse(csv, s_now);
        Assert.Contains(message, Assert.Single(sheet.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Arquivo_em_Latin1_do_Excel_antigo_tambem_le()
    {
        var latin1 = Encoding.Latin1.GetBytes("sku;nome;categoria;custo;estoque\nP-1;Panela de pressão;panelas;99,90;2\n");
        var sheet = ManualCatalogSheet.Parse(ManualCatalogSheet.Decode(latin1), s_now);
        Assert.Equal("Panela de pressão", Assert.Single(sheet.Items).Name);

        var bom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("sku;nome;categoria;custo;estoque\nP-1;Açúcar;casa;5;1\n")).ToArray();
        Assert.Equal("Açúcar", Assert.Single(ManualCatalogSheet.Parse(ManualCatalogSheet.Decode(bom), s_now).Items).Name);
    }

    [Fact]
    public void O_modelo_do_painel_e_uma_planilha_valida() =>
        Assert.True(ManualCatalogSheet.Parse(ManualCatalogSheet.Template, s_now).Ok);
}
