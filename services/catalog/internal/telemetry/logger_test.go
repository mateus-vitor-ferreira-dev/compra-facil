package telemetry

import (
	"bytes"
	"encoding/json"
	"testing"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
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
