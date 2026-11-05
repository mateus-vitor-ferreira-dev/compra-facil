// Package handler expõe a API simulada da Efí (efi.go, #379) e as rotas /sim de demonstração e operação.
package handler

import (
	"crypto/subtle"
	"log/slog"
	"net/http"
	"time"

	"github.com/gin-gonic/gin"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/payment-sim/internal/efi"
)

type Credentials struct {
	// AdminToken vai em X-Sim-Admin-Token nas rotas /sim de operação (listar, liquidar, pagar pela API, resetar),
	// que não existem na Efí e ficam fechadas na demonstração pública (#280)
	AdminToken string
}

type API struct {
	efi   *efi.Provider
	creds Credentials
	log   *slog.Logger
}

func NewRouter(efiProvider *efi.Provider, creds Credentials, log *slog.Logger) *gin.Engine {
	gin.SetMode(gin.ReleaseMode)
	a := &API{efi: efiProvider, creds: creds, log: log}
	r := gin.New()
	r.Use(gin.Recovery(), a.accessLog)
	r.GET("/health", func(c *gin.Context) { c.JSON(http.StatusOK, gin.H{"status": "ok"}) })
	r.POST("/sim/reset", a.requireAdminToken, func(c *gin.Context) { a.efi.Reset(); c.Status(http.StatusNoContent) })
	a.registerEfi(r)
	return r
}

func (a *API) requireAdminToken(c *gin.Context) {
	got := c.GetHeader("X-Sim-Admin-Token")
	if a.creds.AdminToken == "" || subtle.ConstantTimeCompare([]byte(got), []byte(a.creds.AdminToken)) != 1 {
		c.AbortWithStatusJSON(http.StatusUnauthorized, gin.H{"error": "unauthorized", "message": "missing or invalid X-Sim-Admin-Token"})
		return
	}
	c.Next()
}

func (a *API) accessLog(c *gin.Context) {
	start := time.Now()
	c.Next()
	if c.FullPath() != "/health" {
		a.log.Info("requisição", "method", c.Request.Method, "path", c.Request.URL.Path,
			"status", c.Writer.Status(), "duration_ms", time.Since(start).Milliseconds())
	}
}
