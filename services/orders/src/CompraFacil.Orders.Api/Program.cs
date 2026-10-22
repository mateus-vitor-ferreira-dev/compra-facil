using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Orders.Application;
using CompraFacil.Orders.Infrastructure;

// "<api> healthcheck": HEALTHCHECK do container (a imagem chiseled não tem curl)
if (Healthcheck.Requested(args))
{
    return await Healthcheck.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(new ServiceInfo("orders", ContractFile: "orders.v1.yaml"));
builder.AddAdminAuth(audience: "orders"); // /v1/admin/*: o mesmo JWT do catalog (task token -- -aud orders)
builder.Services.AddApplication();
builder.AddInfrastructure();

var app = builder.Build();
app.UseServiceDefaults();
app.MapControllers();
app.MapDeadLetters(); // /v1/admin/dead-letters (ADR 0003)

await app.MigrateDatabaseAsync<OrdersDbContext>();
await app.RunAsync();
return 0;

/// <summary>Visível para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
