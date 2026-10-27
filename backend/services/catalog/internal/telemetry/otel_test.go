package telemetry

import (
	"context"
	"testing"

	"github.com/stretchr/testify/require"
)

// Setup com OTLP ligado não pode falhar na montagem (ex.: schema do semconv diferente do SDK, que
// derrubava o catalog na subida). O endpoint não precisa existir: os exporters conectam sob demanda.
func TestSetup_WithOTLPEndpoint(t *testing.T) {
	t.Setenv("OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:1")

	shutdown, err := Setup(context.Background(), "catalog", "test", "test")
	require.NoError(t, err)

	ctx, cancel := context.WithCancel(context.Background())
	cancel() // não espera o envio para o endpoint inexistente
	_ = shutdown(ctx)
}
