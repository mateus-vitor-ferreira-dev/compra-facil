package handler

import (
	"bytes"
	"encoding/json"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/stretchr/testify/assert"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/payment-sim/internal/efi"
)

type env struct {
	t      *testing.T
	router http.Handler
}

func (e *env) do(method, path string, body any, headers map[string]string) (int, map[string]any) {
	e.t.Helper()
	var raw []byte
	if body != nil {
		raw, _ = json.Marshal(body)
	}
	req := httptest.NewRequestWithContext(e.t.Context(), method, path, bytes.NewReader(raw))
	req.Header.Set("Content-Type", "application/json")
	for k, v := range headers {
		req.Header.Set(k, v)
	}
	rec := httptest.NewRecorder()
	e.router.ServeHTTP(rec, req)
	var out map[string]any
	_ = json.Unmarshal(rec.Body.Bytes(), &out)
	return rec.Code, out
}

func TestOperationRoutes_NeedTheAdminToken(t *testing.T) {
	e := newEfiEnv(t)
	for _, r := range []struct{ method, path string }{
		{http.MethodGet, "/sim/efi/charges"}, {http.MethodGet, "/sim/efi/cobs"}, {http.MethodPost, "/sim/reset"},
	} {
		status, _ := e.do(r.method, r.path, nil, nil)
		assert.Equal(t, http.StatusUnauthorized, status, r.path)
		status, _ = e.do(r.method, r.path, nil, map[string]string{"X-Sim-Admin-Token": "errado"})
		assert.Equal(t, http.StatusUnauthorized, status, r.path)
		status, _ = e.do(r.method, r.path, nil, map[string]string{"X-Sim-Admin-Token": "adm"})
		assert.Contains(t, []int{http.StatusOK, http.StatusNoContent}, status, r.path)
	}

	// sem token configurado, as rotas de operação ficam fechadas para todos
	p := efi.NewProvider(efi.Options{}, nil)
	t.Cleanup(p.Close)
	closed := &env{t: t, router: NewRouter(p, Credentials{}, slog.New(slog.DiscardHandler))}
	status, _ := closed.do(http.MethodGet, "/sim/efi/charges", nil, map[string]string{"X-Sim-Admin-Token": ""})
	assert.Equal(t, http.StatusUnauthorized, status)
}
