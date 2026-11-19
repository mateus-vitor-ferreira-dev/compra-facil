using CompraFacil.Orders.Domain;

namespace CompraFacil.Orders.UnitTests;

public sealed class PersonalizationTests
{
    [Theory]
    [InlineData("  Ana \t Maria \n", "Ana Maria")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Normalize_TrimsAndCollapsesSpaces(string? text, string? expected) =>
        Assert.Equal(expected, Personalization.Normalize(text));

    [Theory]
    [InlineData(null, null, false, true)] // produto sem personalização, sem texto
    [InlineData("Ana", null, false, false)] // produto sem personalização recusa texto
    [InlineData(null, 12, false, true)] // opcional
    [InlineData(null, 12, true, false)] // obrigatória
    [InlineData("Sr. D'Ávila", 12, true, true)]
    [InlineData("Desde 2019!", 12, true, true)]
    [InlineData("Çéü-ñ/?,& 12", 12, true, true)] // doze, no limite
    [InlineData("Ana Carolina S", 12, true, false)] // acima do máximo
    [InlineData("Bia 🎄", 12, true, false)] // emoji
    [InlineData("Анна", 12, true, false)] // outro alfabeto
    [InlineData("a#b", 12, true, false)] // pontuação fora da lista
    public void Accepts_TheSameRulesAsTheCatalog(string? text, int? max, bool required, bool ok) =>
        Assert.Equal(ok, Personalization.Accepts(text, max, required));

    [Fact]
    public void Place_TheSameProductAndSizeWithTwoTexts_IsTwoLines()
    {
        var id = Guid.NewGuid();
        Assert.NotEqual(OrderItem.LineKey(id, "G", "Ana"), OrderItem.LineKey(id, "G", "João"));
        Assert.Equal(OrderItem.LineKey(id, "G", "Ana"), OrderItem.LineKey(id, "g", " Ana "));
        Assert.Equal(OrderItem.LineKey(id, null), OrderItem.LineKey(id, null, "  "));
    }
}
