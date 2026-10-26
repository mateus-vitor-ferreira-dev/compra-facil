using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Identity.Api;
using CompraFacil.Identity.Application;
using CompraFacil.Identity.Application.Tokens;
using CompraFacil.Identity.Infrastructure;
using CompraFacil.Identity.Infrastructure.Persistence;

// "<api> healthcheck": HEALTHCHECK do container (a imagem chiseled não tem curl)
if (Healthcheck.Requested(args))
{
    return await Healthcheck.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(new ServiceInfo("identity", ContractFile: "identity.v1.yaml"));
builder.Services.AddApplication();
builder.AddInfrastructure();
builder.AddOwnTokenValidation(); // GET /v1/me: o identity valida os próprios tokens, sem ir ao JWKS por HTTP

var app = builder.Build();
app.UseServiceDefaults();
app.MapControllers();
app.MapGatewayProblems(); // corpo do 429 do rate limit do gateway

await app.MigrateDatabaseAsync<IdentityDbContext>();
// cria a primeira chave de assinatura (ou rotaciona) antes de aceitar requisições
await app.Services.GetRequiredService<ISigningKeyRing>().CurrentAsync(CancellationToken.None);
await app.SeedAdminAsync();
await app.RunAsync();
return 0;

/// <summary>Visível para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
