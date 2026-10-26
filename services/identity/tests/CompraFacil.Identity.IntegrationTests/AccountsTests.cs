using System.Net;
using CompraFacil.BuildingBlocks.Testing;

namespace CompraFacil.Identity.IntegrationTests;

/// <summary>Cadastro e GET /v1/me.</summary>
public sealed class AccountsTests(IdentityFixture fx) : IClassFixture<IdentityFixture>
{
    private readonly IdentityClient _api = new(fx.Factory.CreateClient());

    [Fact]
    public async Task Register_Returns201Customer_FollowingTheContract()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";

        using var response = await _api.RegisterAsync(email, name: "  Ana Souza ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/v1/me", response.Headers.Location?.OriginalString);
        var body = await IdentityClient.JsonOf(response);
        ContractAssert.AssertSchema("identity.v1.yaml", "/components/schemas/Account", body.GetRawText());
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal("Ana Souza", body.GetProperty("name").GetString());
        Assert.Equal(["customer"], body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.False(body.TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Register_SameEmailInOtherCase_Is409()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        using var first = await _api.RegisterAsync(email);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var again = await _api.RegisterAsync(email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("application/problem+json", again.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Register_SameEmailAtTheSameTime_OneCreated_OthersAre409()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _api.RegisterAsync(email)));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created),
                r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        }
        finally
        {
            foreach (var r in responses)
            {
                r.Dispose();
            }
        }
    }

    [Theory]
    [InlineData("ana@exemplo.com", "curta", "password")]          // menos de 8
    [InlineData("ana@exemplo.com", "aaaabbbb", "password")]       // só 2 caracteres diferentes
    [InlineData("nao-e-email", "senha-boa-123", "email")]
    [InlineData("", "senha-boa-123", "email")]
    public async Task Register_Invalid_Is422OnTheField(string email, string password, string field)
    {
        using var response = await _api.RegisterAsync(email, password);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await IdentityClient.JsonOf(response);
        ContractAssert.AssertSchema("common/v1/common.yaml", "/components/schemas/ValidationProblem", body.GetRawText());
        Assert.Contains(body.GetProperty("errors").EnumerateArray(), e => e.GetProperty("field").GetString() == field);
    }

    [Fact]
    public async Task Me_ReturnsTheAccountOfTheToken()
    {
        var email = $"{Guid.NewGuid():N}@exemplo.com";
        var (access, _) = await _api.NewSessionAsync(email);

        using var response = await _api.MeAsync(access);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await IdentityClient.JsonOf(response);
        ContractAssert.AssertSchema("identity.v1.yaml", "/components/schemas/Account", body.GetRawText());
        Assert.Equal(email, body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Me_WithoutToken_Is401ProblemDetails()
    {
        using var response = await _api.MeAsync(null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Admin_FromConfiguration_HasTheAdminRole()
    {
        using var login = await _api.LoginAsync(IdentityFixture.AdminEmail, IdentityFixture.AdminPassword);
        var (access, _) = IdentityClient.Tokens(await IdentityClient.JsonOf(login));

        using var me = await _api.MeAsync(access);

        var roles = (await IdentityClient.JsonOf(me)).GetProperty("roles").EnumerateArray().Select(r => r.GetString());
        Assert.Equal(["admin"], roles);
    }
}
