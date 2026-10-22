using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace CompraFacil.BuildingBlocks.Web;

/// <summary>
/// Serve o contrato (fonte da verdade, contracts/openapi) em /openapi e o Swagger UI em /docs, igual
/// ao catalog. O serviço implementa o contrato; não há OpenAPI gerado a partir do código.
/// Os arquivos chegam ao diretório da aplicação pelo .csproj da Api (Content com Link).
/// </summary>
internal static class ContractDocs
{
    // mesma versão fixa do catalog (services/catalog/internal/handler/docs.go)
    private const string SwaggerUiVersion = "5.34.0";

    public static void Map(WebApplication app, ServiceInfo service)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "openapi");
        if (!File.Exists(Path.Combine(dir, service.ContractFile!)))
        {
            throw new InvalidOperationException(
                $"contrato {service.ContractFile} não encontrado em {dir}; confira o <Content Link=\"openapi/...\"> da Api");
        }

        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".yaml"] = "application/yaml";
        // o $ref ./common/v1/common.yaml resolve relativo a /openapi/<serviço>.v1.yaml
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(dir),
            RequestPath = "/openapi",
            ContentTypeProvider = types,
        });

        // URL relativa do spec: funciona direto (/docs) e atrás do gateway (/orders/docs)
        var page = $$"""
            <!doctype html>
            <html lang="pt-BR">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{service.Name}} API — CompraFácil</title>
              <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@{{SwaggerUiVersion}}/swagger-ui.css">
            </head>
            <body>
              <div id="swagger-ui"></div>
              <script src="https://unpkg.com/swagger-ui-dist@{{SwaggerUiVersion}}/swagger-ui-bundle.js" crossorigin></script>
              <script>
                window.ui = SwaggerUIBundle({ url: "openapi/{{service.ContractFile}}", dom_id: "#swagger-ui", deepLinking: true });
              </script>
            </body>
            </html>
            """;
        app.MapGet("/docs", () => Results.Content(page, "text/html; charset=utf-8")).ExcludeFromDescription();
    }
}
