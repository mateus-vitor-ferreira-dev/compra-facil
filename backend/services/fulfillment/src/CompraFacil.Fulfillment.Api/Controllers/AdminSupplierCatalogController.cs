using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Fulfillment.Application.SupplierCatalog;
using CompraFacil.Fulfillment.Application.SupplierOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Fulfillment.Api.Controllers;

/// <summary>syncSupplierCatalog (contracts/openapi/fulfillment.v1.yaml): o painel pede a leitura do feed na hora.</summary>
[ApiController]
[Produces("application/json")]
[Authorize(Policy = JwtAuth.AdminPolicy)]
public sealed class AdminSupplierCatalogController(CatalogSync sync) : ControllerBase
{
    [HttpPost("v1/admin/supplier-catalog/sync")]
    public async Task<CatalogSyncResult> Sync(CancellationToken ct)
    {
        try
        {
            return await sync.RunAsync(ct);
        }
        catch (CatalogSyncBusyException e)
        {
            throw new ConflictException("Sincronização em andamento", e.Message);
        }
        catch (SupplierUnavailableException e)
        {
            throw new DependencyUnavailableException($"o fornecedor não respondeu: {e.Message}", TimeSpan.FromSeconds(30));
        }
    }
}
