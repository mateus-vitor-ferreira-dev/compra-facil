package handler

import (
	"log/slog"
	"net/http"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
)

const (
	HeaderRequestID = "X-Request-ID"
	ctxRequestID    = "request_id"
)

// RequestID reaproveita o X-Request-ID recebido (ex.: do gateway) ou gera um novo.
func RequestID() gin.HandlerFunc {
	return func(c *gin.Context) {
		id := c.GetHeader(HeaderRequestID)
		if id == "" || len(id) > 128 {
			id = uuid.NewString()
		}
		c.Set(ctxRequestID, id)
		c.Header(HeaderRequestID, id)
		c.Next()
	}
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

		log.LogAttrs(c.Request.Context(), level, "http request",
			slog.String("method", c.Request.Method),
			slog.String("path", c.FullPath()),
			slog.String("uri", c.Request.URL.RequestURI()),
			slog.Int("status", status),
			slog.Float64("latency_ms", float64(time.Since(start).Microseconds())/1000),
			slog.String("client_ip", c.ClientIP()),
			slog.String(ctxRequestID, c.GetString(ctxRequestID)),
		)
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
