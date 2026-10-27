package handler

import (
	"log/slog"
	"net/http"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"go.opentelemetry.io/otel/trace"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/events"
)

const (
	HeaderRequestID = "X-Request-ID"
	// HeaderTraceID devolve o trace W3C da requisição, para ir da resposta direto ao trace no Grafana.
	HeaderTraceID = "X-Trace-Id"
	ctxRequestID  = "request_id"
)

// RequestID reaproveita o X-Request-ID recebido (ex.: do gateway) ou gera um novo. Ele também vira
// o correlation_id dos eventos que a requisição gerar.
func RequestID() gin.HandlerFunc {
	return func(c *gin.Context) {
		id := c.GetHeader(HeaderRequestID)
		if id == "" || len(id) > 128 {
			id = uuid.NewString()
		}
		c.Set(ctxRequestID, id)
		c.Header(HeaderRequestID, id)
		if traceID := traceIDOf(c); traceID != "" {
			c.Header(HeaderTraceID, traceID)
		}
		c.Request = c.Request.WithContext(events.WithCorrelationID(c.Request.Context(), id))
		c.Next()
	}
}

// traceIDOf devolve o trace da requisição (o span do otelgin); vazio sem tracing ou fora da amostragem.
func traceIDOf(c *gin.Context) string {
	if sc := trace.SpanContextFromContext(c.Request.Context()); sc.IsValid() && sc.IsSampled() {
		return sc.TraceID().String()
	}
	return ""
}

// Logger registra uma linha JSON por requisição.
func Logger(log *slog.Logger) gin.HandlerFunc {
	return func(c *gin.Context) {
		start := time.Now()
		c.Next()

		status := c.Writer.Status()
		level := slog.LevelInfo
		switch {
		case status >= http.StatusInternalServerError:
			level = slog.LevelError
		case status >= http.StatusBadRequest:
			level = slog.LevelWarn
		}

		attrs := []slog.Attr{
			slog.String("method", c.Request.Method),
			slog.String("path", c.FullPath()),
			slog.String("uri", c.Request.URL.RequestURI()),
			slog.Int("status", status),
			slog.Float64("latency_ms", float64(time.Since(start).Microseconds())/1000),
			slog.String("client_ip", c.ClientIP()),
			slog.String(ctxRequestID, c.GetString(ctxRequestID)),
		}
		if sub := c.GetString(ctxSubject); sub != "" {
			attrs = append(attrs, slog.String(ctxSubject, sub)) // operador autenticado (rotas admin)
		}
		log.LogAttrs(c.Request.Context(), level, "http request", attrs...)
	}
}

// Recovery transforma panics em 500 no formato ProblemDetails, sem derrubar o processo.
func Recovery(log *slog.Logger) gin.HandlerFunc {
	return gin.CustomRecoveryWithWriter(nil, func(c *gin.Context, err any) {
		log.ErrorContext(c.Request.Context(), "panic recuperado",
			slog.Any("error", err),
			slog.String(ctxRequestID, c.GetString(ctxRequestID)),
		)
		WriteProblem(c, http.StatusInternalServerError, "Erro interno", "")
	})
}
