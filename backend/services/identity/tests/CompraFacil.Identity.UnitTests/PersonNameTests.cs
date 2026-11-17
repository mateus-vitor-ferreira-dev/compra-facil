using CompraFacil.Identity.Domain;

namespace CompraFacil.Identity.UnitTests;

public sealed class PersonNameTests
{
    [Theory]
    [InlineData("Ana Maria")]
    [InlineData("João d'Ávila")]
    [InlineData("João d’Ávila")]
    [InlineData("Maria-Clara")]
    [InlineData("J. R. Silva")]
    [InlineData("José Antônio dos Santos Júnior")]
    [InlineData("Conceição Ñuñez")]
    [InlineData("Jr.")]
    public void RealNames_AreAccepted(string name) => Assert.True(PersonName.IsValid(name));

    [Theory]
    [InlineData("https://embrulhado-seguranca.com/verificar")]
    [InlineData("Ana https://x")]
    [InlineData("embrulhado-seguranca.com")]
    [InlineData("evil.com")]
    [InlineData("Ana evil.com.br")]
    [InlineData("www.x")]
    [InlineData("Ana WWW.x")]
    [InlineData("ana@exemplo.com")]
    [InlineData("a/b")]
    [InlineData("pix: 123")]
    [InlineData("Ana 2")]
    [InlineData("<b>Ana</b>")]
    [InlineData(". - '")]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public void LinksEmailsAndDomains_AreRejected(string? name) => Assert.False(PersonName.IsValid(name));

    [Fact]
    public void LongerThanTheColumn_IsRejected()
    {
        Assert.True(PersonName.IsValid(new string('a', User.MaxNameLength)));
        Assert.False(PersonName.IsValid(new string('a', User.MaxNameLength + 1)));
    }

    [Fact]
    public void Normalize_TrimsAndCollapsesSpaces() =>
        Assert.Equal("Ana Maria Souza", PersonName.Normalize("  Ana \t Maria\n  Souza  "));

    [Theory]
    [InlineData("Ana do Google", "ana@exemplo.com", "Ana do Google")]
    [InlineData("  Ana   Lima ", "ana@exemplo.com", "Ana Lima")]
    [InlineData("https://evil.com/verificar", "joao.silva@exemplo.com", "joao silva")]
    [InlineData("evil.com", "maria_clara-2@exemplo.com", "maria clara")]
    [InlineData("evil.com", "12345@exemplo.com", PersonName.Fallback)]
    [InlineData(null, "", PersonName.Fallback)]
    public void Sanitize_NeverFails_AndNeverKeepsALink(string? name, string email, string expected) =>
        Assert.Equal(expected, PersonName.Sanitize(name, email));

    [Fact]
    public void User_StoresTheNormalizedName()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("Ana Maria", User.Create("ana@exemplo.com", " Ana   Maria ", now).Name);
        Assert.Equal("Bia Lima", User.CreateGuest("bia@exemplo.com", "BIA@EXEMPLO.COM", "Bia  Lima", Guid.NewGuid(), now).Name);
    }
}
