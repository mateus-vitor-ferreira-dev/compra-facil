// Package handler contém as rotas HTTP (Gin) do catálogo.
package handler

import (
	"log/slog"
	"net/http"

	"github.com/gin-gonic/gin"
)

// NewRouter monta o engine Gin com os middlewares padrão e as rotas de saúde.
func NewRouter(log *slog.Logger, health *Health) *gin.Engine {
	gin.SetMode(gin.ReleaseMode)

	r := gin.New()
	r.ContextWithFallback = true // c.Request.Context() chega aos services e ao GORM
	r.Use(RequestID(), Logger(log), Recovery(log))

	r.GET("/health", health.Live)
	r.GET("/ready", health.Ready)

	r.NoRoute(func(c *gin.Context) {
		WriteProblem(c, http.StatusNotFound, "Recurso não encontrado", "")
	})

	return r
}
