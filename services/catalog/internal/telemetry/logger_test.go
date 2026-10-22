package telemetry

import (
	"bytes"
	"context"
	"encoding/json"
	"strings"
	"testing"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"go.opentelemetry.io/otel/trace"
)

func TestNewLogger_JSONWithServiceAttrs(t *testing.T) {
	var buf bytes.Buffer
	log := NewLogger(&buf, "warn", "catalog", "test")

	log.Info("ignorado pelo nível")
	log.Warn("registrado", "sku", "ABC-1")

	var line map[string]any
	require.NoError(t, json.Unmarshal(buf.Bytes(), &line), "uma única linha JSON")
	assert.Equal(t, "registrado", line["msg"])
	assert.Equal(t, "catalog", line["service"])
	assert.Equal(t, "test", line["env"])
	assert.Equal(t, "ABC-1", line["sku"])
}

func TestParseLevel(t *testing.T) {
	for in, want := range map[string]string{"debug": "DEBUG", "WARN": "WARN", "error": "ERROR", "x": "INFO"} {
		assert.Equal(t, want, parseLevel(in).String(), in)
	}
}

func TestNewLogger_AddsTraceIDFromContext(t *testing.T) {
	var buf bytes.Buffer
	log := NewLogger(&buf, "info", "catalog", "test")
	sc := trace.NewSpanContext(trace.SpanContextConfig{
		TraceID:    trace.TraceID{0x4b, 0xf9, 0x2f, 0x35, 0x77, 0xb3, 0x4d, 0xa6, 0xa3, 0xce, 0x92, 0x9d, 0x0e, 0x0e, 0x47, 0x36},
		SpanID:     trace.SpanID{0x00, 0xf0, 0x67, 0xaa, 0x0b, 0xa9, 0x02, 0xb7},
		TraceFlags: trace.FlagsSampled,
	})

	log.InfoContext(trace.ContextWithSpanContext(context.Background(), sc), "com span")
	log.Info("sem span")

	lines := strings.Split(strings.TrimSpace(buf.String()), "\n")
	require.Len(t, lines, 2)
	assert.Contains(t, lines[0], `"trace_id":"4bf92f3577b34da6a3ce929d0e0e4736"`)
	assert.Contains(t, lines[0], `"span_id":"00f067aa0ba902b7"`)
	assert.NotContains(t, lines[1], "trace_id")
}
