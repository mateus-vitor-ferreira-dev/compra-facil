// Package telemetry concentra logs, traces e métricas OpenTelemetry do catálogo.
package telemetry

import (
	"context"
	"errors"
	"io"
	"log/slog"
	"strings"

	"go.opentelemetry.io/otel/trace"
)

// NewLogger cria um logger slog em JSON com os atributos fixos do serviço e, quando há span no
// contexto, trace_id/span_id (para ir do log ao trace). Com OTLP ligado (Setup antes), cada
// registro também vai para o Collector → Loki.
func NewLogger(w io.Writer, level, service, env string) *slog.Logger {
	lvl := parseLevel(level)
	var h slog.Handler = traceHandler{slog.NewJSONHandler(w, &slog.HandlerOptions{Level: lvl})}
	if Enabled() {
		h = fanout{h, leveled{otelHandler(service), lvl}}
	}
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

// traceHandler acrescenta trace_id e span_id do span ativo no contexto.
type traceHandler struct{ slog.Handler }

func (h traceHandler) Handle(ctx context.Context, r slog.Record) error {
	if sc := trace.SpanContextFromContext(ctx); sc.IsValid() {
		r.AddAttrs(slog.String("trace_id", sc.TraceID().String()), slog.String("span_id", sc.SpanID().String()))
	}
	return h.Handler.Handle(ctx, r) //nolint:wrapcheck // repassa o erro do handler embrulhado
}

func (h traceHandler) WithAttrs(attrs []slog.Attr) slog.Handler {
	return traceHandler{h.Handler.WithAttrs(attrs)}
}

func (h traceHandler) WithGroup(name string) slog.Handler {
	return traceHandler{h.Handler.WithGroup(name)}
}

// leveled aplica o nível mínimo a um handler que não tem opção de nível (o do OTel).
type leveled struct {
	slog.Handler
	min slog.Level
}

func (h leveled) Enabled(ctx context.Context, l slog.Level) bool {
	return l >= h.min && h.Handler.Enabled(ctx, l)
}

func (h leveled) WithAttrs(attrs []slog.Attr) slog.Handler {
	return leveled{h.Handler.WithAttrs(attrs), h.min}
}

func (h leveled) WithGroup(name string) slog.Handler {
	return leveled{h.Handler.WithGroup(name), h.min}
}

// fanout manda o mesmo registro para vários handlers (stdout e OTLP).
type fanout []slog.Handler

func (f fanout) Enabled(ctx context.Context, l slog.Level) bool {
	for _, h := range f {
		if h.Enabled(ctx, l) {
			return true
		}
	}
	return false
}

func (f fanout) Handle(ctx context.Context, r slog.Record) error {
	var errs []error
	for _, h := range f {
		if h.Enabled(ctx, r.Level) {
			errs = append(errs, h.Handle(ctx, r.Clone()))
		}
	}
	return errors.Join(errs...) //nolint:wrapcheck // só agrega os erros dos handlers
}

func (f fanout) WithAttrs(attrs []slog.Attr) slog.Handler {
	out := make(fanout, len(f))
	for i, h := range f {
		out[i] = h.WithAttrs(attrs)
	}
	return out
}

func (f fanout) WithGroup(name string) slog.Handler {
	out := make(fanout, len(f))
	for i, h := range f {
		out[i] = h.WithGroup(name)
	}
	return out
}
