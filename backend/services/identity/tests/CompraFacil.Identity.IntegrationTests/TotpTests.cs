using System.Text;
using CompraFacil.Identity.Application.Accounts;

namespace CompraFacil.Identity.IntegrationTests;

/// <summary>O TOTP contra os vetores da RFC 6238 e os códigos de recuperação (sem banco).</summary>
public sealed class TotpTests
{
    // RFC 6238, apêndice B: chave ASCII "12345678901234567890", SHA-1; os 6 últimos dígitos dos 8 do vetor
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    [InlineData(20000000000L, "353130")]
    public void Code_MatchesTheRfcVectors(long unixSeconds, string expected)
    {
        var key = Encoding.ASCII.GetBytes("12345678901234567890");

        Assert.Equal(expected, Totp.Code(key, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))));
    }

    [Fact]
    public void Base32_ReadsTheKeyInAnyCase_AndWithTheGroupsShownOnScreen()
    {
        // "12345678901234567890" em Base32
        const string key = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

        Assert.Equal("12345678901234567890", Encoding.ASCII.GetString(Totp.FromBase32(key)));
        Assert.Equal(Totp.FromBase32(key), Totp.FromBase32(Totp.Grouped(key)));
        Assert.Equal("gezd gnbv gy3t qojq gezd gnbv gy3t qojq", Totp.Grouped(key));
    }

    [Theory]
    [InlineData("123456", "123456")]
    [InlineData(" 123 456 ", "123456")]
    [InlineData("123-456", "123456")]
    [InlineData("12345", null)]
    [InlineData("1234567", null)]
    [InlineData("12345a", null)]
    [InlineData("１２３４５６", null)] // dígitos de largura total não são ASCII
    public void Normalize_KeepsOnlySixAsciiDigits(string input, string? expected) =>
        Assert.Equal(expected, Totp.Normalize(input));

    [Fact]
    public void AuthenticatorUri_HasTheIssuerAndTheAccount()
    {
        var uri = Totp.AuthenticatorUri("ana+ops@exemplo.com", "ABCD");

        Assert.Equal("otpauth://totp/Embrulhado:ana%2Bops%40exemplo.com?secret=ABCD&issuer=Embrulhado&algorithm=SHA1&digits=6&period=30", uri);
    }

    [Fact]
    public void RecoveryCodes_TenDistinct_HashIgnoresCaseAndHyphen()
    {
        var codes = RecoveryCodes.New();

        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, c => Assert.True(RecoveryCodes.LooksValid(c)));
        var code = codes[0];
        Assert.Equal(RecoveryCodes.Hash(code), RecoveryCodes.Hash(code.ToUpperInvariant().Replace("-", " ", StringComparison.Ordinal)));
        Assert.DoesNotContain(code.Replace("-", "", StringComparison.Ordinal), RecoveryCodes.Hash(code), StringComparison.Ordinal);
        Assert.False(RecoveryCodes.LooksValid("curto"));
        Assert.False(RecoveryCodes.LooksValid("iiiii-lllll")); // fora do alfabeto (i, l, o, u)
    }
}
