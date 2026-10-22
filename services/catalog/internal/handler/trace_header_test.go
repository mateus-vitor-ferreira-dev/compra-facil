package handler

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/gin-gonic/gin"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"go.opentelemetry.io/contrib/instrumentation/github.com/gin-gonic/gin/otelgin"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
)

// Com tracing, a resposta traz o trace W3C (X-Trace-Id) e o ProblemDetails o mesmo trace_id: o
// cliente (ou o front) vai da resposta direto ao trace no Grafana.
func TestTraceID_InHeaderAndProblem(t *testing.T) {
	gin.SetMode(gin.TestMode)
	tp := sdktrace.NewTracerProvider(sdktrace.WithSampler(sdktrace.AlwaysSample()))
	t.Cleanup(func() { _ = tp.Shutdown(t.Context()) })

	r := gin.New()
	r.Use(otelgin.Middleware("catalog", otelgin.WithTracerProvider(tp)), RequestID())
	r.GET("/falha", func(c *gin.Context) { WriteProblem(c, http.StatusNotFound, "Recurso não encontrado", "") })

	w := httptest.NewRecorder()
	r.ServeHTTP(w, httptest.NewRequestWithContext(t.Context(), http.MethodGet, "/falha", nil))

	traceID := w.Header().Get(HeaderTraceID)
	assert.Regexp(t, `^[0-9a-f]{32}$`, traceID)
	var problem Problem
	require.NoError(t, json.Unmarshal(w.Body.Bytes(), &problem))
	assert.Equal(t, traceID, problem.TraceID)
}

// Sem tracing (OTel desligado), nada de header vazio nem trace_id inventado.
func TestTraceID_AbsentWithoutTracing(t *testing.T) {
	gin.SetMode(gin.TestMode)
	r := gin.New()
	r.Use(RequestID())
	r.GET("/falha", func(c *gin.Context) { WriteProblem(c, http.StatusNotFound, "Recurso não encontrado", "") })

	w := httptest.NewRecorder()
	r.ServeHTTP(w, httptest.NewRequestWithContext(t.Context(), http.MethodGet, "/falha", nil))

	assert.Empty(t, w.Header().Get(HeaderTraceID))
	assert.NotContains(t, w.Body.String(), "trace_id")
}
