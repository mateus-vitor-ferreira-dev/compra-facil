// Package handler contém as rotas HTTP (Gin) do catálogo.
package handler

import (
	"log/slog"
	"net/http"

	"github.com/gin-gonic/gin"
	"go.opentelemetry.io/contrib/instrumentation/github.com/gin-gonic/gin/otelgin"
)

// Registrar é um grupo de rotas (ex.: CatalogHandler, Docs) plugado no router.
type Registrar interface {
	Register(r gin.IRouter)
}

// NewRouter monta o engine Gin com os middlewares padrão, as rotas de saúde e os registrars.
func NewRouter(log *slog.Logger, health *Health, registrars ...Registrar) *gin.Engine {
	gin.SetMode(gin.ReleaseMode)

	r := gin.New()
	r.ContextWithFallback = true // c.Request.Context() chega aos services e ao GORM
	// otelgin primeiro: o span já existe quando o log da requisição é escrito (trace_id no log)
	r.Use(otelgin.Middleware("catalog", otelgin.WithFilter(func(req *http.Request) bool {
		return req.URL.Path != "/health" && req.URL.Path != "/ready" // sondas não viram trace
	})))
	r.Use(RequestID(), Logger(log), Recovery(log))

	r.GET("/health", health.Live)
	r.GET("/ready", health.Ready)
	r.GET("/problems/:status", GatewayProblem)
	for _, reg := range registrars {
		reg.Register(r)
	}

	r.NoRoute(func(c *gin.Context) {
		WriteProblem(c, http.StatusNotFound, "Recurso não encontrado", "")
	})

	return r
}
