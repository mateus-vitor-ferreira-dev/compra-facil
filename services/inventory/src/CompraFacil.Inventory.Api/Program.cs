using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Inventory.Application;
using CompraFacil.Inventory.Infrastructure;

// "<api> healthcheck": HEALTHCHECK do container (a imagem chiseled não tem curl)
if (Healthcheck.Requested(args))
{
    return await Healthcheck.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(new ServiceInfo("inventory", ContractFile: "inventory.v1.yaml"));
builder.AddAdminAuth(audience: "inventory"); // /v1/admin/*: o mesmo JWT do catalog (task token -- -aud inventory)
builder.Services.AddApplication();
builder.AddInfrastructure();

var app = builder.Build();
app.UseServiceDefaults();
app.MapControllers();

await app.MigrateDatabaseAsync<InventoryDbContext>();
await app.RunAsync();
return 0;

/// <summary>Visível para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
