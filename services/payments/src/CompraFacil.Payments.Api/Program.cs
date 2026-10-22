using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Payments.Application;
using CompraFacil.Payments.Infrastructure;

// "<api> healthcheck": HEALTHCHECK do container (a imagem chiseled não tem curl)
if (Healthcheck.Requested(args))
{
    return await Healthcheck.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(new ServiceInfo("payments", ContractFile: "payments.v1.yaml"));
builder.Services.AddApplication();
builder.AddInfrastructure();

var app = builder.Build();
app.UseServiceDefaults();
app.MapControllers();

await app.MigrateDatabaseAsync<PaymentsDbContext>();
await app.RunAsync();
return 0;

/// <summary>Visível para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
