package handler

import (
	"io/fs"
	"net/http"

	"github.com/gin-gonic/gin"
)

// swaggerUIVersion fixa a versão do swagger-ui-dist carregado do CDN (só a página /docs usa).
const swaggerUIVersion = "5.34.0"

const docsPage = `<!doctype html>
<html lang="pt-BR">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Catalog API — CompraFácil</title>
  <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@` + swaggerUIVersion + `/swagger-ui.css">
</head>
<body>
  <div id="swagger-ui"></div>
  <script src="https://unpkg.com/swagger-ui-dist@` + swaggerUIVersion + `/swagger-ui-bundle.js" crossorigin></script>
  <script>
    window.ui = SwaggerUIBundle({ url: "openapi/catalog.v1.yaml", dom_id: "#swagger-ui", deepLinking: true });
  </script>
</body>
</html>`

// Docs serve o contrato (fonte da verdade, embutido) e o Swagger UI em /docs.
type Docs struct{ files fs.FS }

func NewDocs(files fs.FS) *Docs { return &Docs{files: files} }

func (d *Docs) Register(r gin.IRouter) {
	r.GET("/docs", func(c *gin.Context) {
		// URL relativa do spec: funciona direto (/docs) e atrás do gateway (/catalog/docs)
		c.Data(http.StatusOK, "text/html; charset=utf-8", []byte(docsPage))
	})
	// o $ref ./common/v1/common.yaml resolve relativo a /openapi/catalog.v1.yaml
	r.StaticFS("/openapi", http.FS(d.files))
}
