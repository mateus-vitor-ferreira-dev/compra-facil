using CompraFacil.BuildingBlocks.Web;

namespace CompraFacil.BuildingBlocks.Tests;

public sealed class FieldPathTests
{
    [Theory]
    [InlineData("CustomerEmail", "customer_email")]
    [InlineData("Items[0].ProductId", "items[0].product_id")]
    [InlineData("Items[12].ExpectedUnitPriceCents", "items[12].expected_unit_price_cents")]
    [InlineData("Address.ZipCode", "address.zip_code")]
    [InlineData("", "")]
    public void ToSnakeCase_FollowsTheJsonPath(string path, string expected) =>
        Assert.Equal(expected, FieldPath.ToSnakeCase(path));
}
