package handler

import (
	"context"
	"net/http"
	"sync/atomic"
	"time"

	"github.com/gin-gonic/gin"
)

// Check verifica uma dependência (Postgres, Redis, Kafka...). Retorna erro se ela não está pronta.
type Check func(ctx context.Context) error

// Health expõe /health (liveness: o processo está vivo) e /ready (readiness: pode receber tráfego).
type Health struct {
	checks       map[string]Check
	shuttingDown atomic.Bool
	timeout      time.Duration
}

func NewHealth() *Health {
	return &Health{checks: map[string]Check{}, timeout: 2 * time.Second}
}

// AddCheck registra uma dependência avaliada no /ready.
func (h *Health) AddCheck(name string, check Check) { h.checks[name] = check }

// MarkShuttingDown faz o /ready responder 503, para o balanceador parar de mandar tráfego
// enquanto as requisições em andamento terminam.
func (h *Health) MarkShuttingDown() { h.shuttingDown.Store(true) }

func (h *Health) Live(c *gin.Context) {
	c.JSON(http.StatusOK, gin.H{"status": "ok"})
}

func (h *Health) Ready(c *gin.Context) {
	if h.shuttingDown.Load() {
		c.JSON(http.StatusServiceUnavailable, gin.H{"status": "shutting_down"})
		return
	}

	ctx, cancel := context.WithTimeout(c.Request.Context(), h.timeout)
	defer cancel()

	status, results := http.StatusOK, gin.H{}
	for name, check := range h.checks {
		if err := check(ctx); err != nil {
			status = http.StatusServiceUnavailable
			results[name] = err.Error()
			continue
		}
		results[name] = "ok"
	}

	body := gin.H{"status": "ok", "checks": results}
	if status != http.StatusOK {
		body["status"] = "unavailable"
	}
	c.JSON(status, body)
}
