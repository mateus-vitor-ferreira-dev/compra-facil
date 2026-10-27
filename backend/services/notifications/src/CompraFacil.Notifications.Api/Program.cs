using CompraFacil.BuildingBlocks.Data;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Notifications.Application;
using CompraFacil.Notifications.Infrastructure;

// "<api> healthcheck": HEALTHCHECK do container (a imagem chiseled não tem curl)
if (Healthcheck.Requested(args))
{
    return await Healthcheck.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(new ServiceInfo("notifications", ContractFile: null));
builder.AddJwtAuth(); // /v1/admin/*: token de operador do identity, validado pelo JWKS (Auth:Authority)
builder.Services.AddApplication();
builder.AddInfrastructure();

var app = builder.Build();
app.UseServiceDefaults();
app.MapControllers();
app.MapDeadLetters(); // /v1/admin/dead-letters (ADR 0003)

await app.MigrateDatabaseAsync<NotificationsDbContext>();
await app.RunAsync();
return 0;

/// <summary>Visível para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
