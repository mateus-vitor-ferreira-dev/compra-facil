// Package telemetry concentra logs (e, mais adiante, traces e métricas OpenTelemetry).
package telemetry

import (
	"io"
	"log/slog"
	"strings"
)

// NewLogger cria um logger slog em JSON com os atributos fixos do serviço.
func NewLogger(w io.Writer, level, service, env string) *slog.Logger {
	h := slog.NewJSONHandler(w, &slog.HandlerOptions{Level: parseLevel(level)})
	return slog.New(h).With("service", service, "env", env)
}

func parseLevel(level string) slog.Level {
	switch strings.ToLower(level) {
	case "debug":
		return slog.LevelDebug
	case "warn":
		return slog.LevelWarn
	case "error":
		return slog.LevelError
	default:
		return slog.LevelInfo
	}
}
