package handler

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/gin-gonic/gin"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func do(t *testing.T, r http.Handler, method, path string, headers map[string]string) *httptest.ResponseRecorder {
	t.Helper()
	req := httptest.NewRequestWithContext(context.Background(), method, path, nil)
	for k, v := range headers {
		req.Header.Set(k, v)
	}
	w := httptest.NewRecorder()
	r.ServeHTTP(w, req)
	return w
}

func decode(t *testing.T, w *httptest.ResponseRecorder) map[string]any {
	t.Helper()
	var body map[string]any
	require.NoError(t, json.Unmarshal(w.Body.Bytes(), &body))
	return body
}

func discardLogger() *slog.Logger { return slog.New(slog.DiscardHandler) }

func TestHealth_Live(t *testing.T) {
	r := NewRouter(discardLogger(), NewHealth())

	w := do(t, r, http.MethodGet, "/health", nil)

	assert.Equal(t, http.StatusOK, w.Code)
	assert.Equal(t, "ok", decode(t, w)["status"])
}

func TestHealth_Ready(t *testing.T) {
	tests := []struct {
		name       string
		checks     map[string]Check
		shutdown   bool
		wantStatus int
		wantBody   string
	}{
		{name: "sem dependências", wantStatus: http.StatusOK, wantBody: "ok"},
		{
			name:       "dependência ok",
			checks:     map[string]Check{"postgres": func(context.Context) error { return nil }},
			wantStatus: http.StatusOK, wantBody: "ok",
		},
		{
			name: "dependência fora",
			checks: map[string]Check{
				"postgres": func(context.Context) error { return nil },
				"redis":    func(context.Context) error { return errors.New("connection refused") },
			},
			wantStatus: http.StatusServiceUnavailable, wantBody: "unavailable",
		},
		{name: "desligando", shutdown: true, wantStatus: http.StatusServiceUnavailable, wantBody: "shutting_down"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			h := NewHealth()
			for name, c := range tt.checks {
				h.AddCheck(name, c)
			}
			if tt.shutdown {
				h.MarkShuttingDown()
			}

			w := do(t, NewRouter(discardLogger(), h), http.MethodGet, "/ready", nil)

			assert.Equal(t, tt.wantStatus, w.Code)
			assert.Equal(t, tt.wantBody, decode(t, w)["status"])
		})
	}
}

func TestRequestID(t *testing.T) {
	r := NewRouter(discardLogger(), NewHealth())

	t.Run("gera quando ausente", func(t *testing.T) {
		w := do(t, r, http.MethodGet, "/health", nil)
		assert.Len(t, w.Header().Get(HeaderRequestID), 36)
	})
	t.Run("propaga o recebido", func(t *testing.T) {
		w := do(t, r, http.MethodGet, "/health", map[string]string{HeaderRequestID: "abc-123"})
		assert.Equal(t, "abc-123", w.Header().Get(HeaderRequestID))
	})
}

func TestRecovery_ReturnsProblemDetails(t *testing.T) {
	var logs bytes.Buffer
	log := slog.New(slog.NewJSONHandler(&logs, nil))
	r := NewRouter(log, NewHealth())
	r.GET("/boom", func(*gin.Context) { panic("falhou") })

	w := do(t, r, http.MethodGet, "/boom", nil)

	assert.Equal(t, http.StatusInternalServerError, w.Code)
	assert.Equal(t, "application/problem+json", w.Header().Get("Content-Type"))
	body := decode(t, w)
	assert.InDelta(t, 500, body["status"], 0)
	assert.Equal(t, w.Header().Get(HeaderRequestID), body["request_id"])
	assert.Contains(t, logs.String(), "panic recuperado")
}

func TestNoRoute(t *testing.T) {
	w := do(t, NewRouter(discardLogger(), NewHealth()), http.MethodGet, "/nao-existe", nil)

	assert.Equal(t, http.StatusNotFound, w.Code)
	assert.Equal(t, "/nao-existe", decode(t, w)["instance"])
}
