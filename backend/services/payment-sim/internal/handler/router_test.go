package handler

import (
	"bytes"
	"encoding/json"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strconv"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/payment-sim/internal/payment"
)

type env struct {
	t      *testing.T
	router http.Handler
}

func newEnv(t *testing.T) *env {
	t.Helper()
	p := payment.NewProvider(payment.Options{}, nil)
	t.Cleanup(p.Close)
	return &env{t: t, router: NewRouter(p, Credentials{AccessToken: "tok", PublicKey: "pub"}, slog.New(slog.DiscardHandler))}
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

func auth(key string) map[string]string {
	return map[string]string{"Authorization": "Bearer tok", "X-Idempotency-Key": key}
}

func TestPIXFlow_OverHTTP(t *testing.T) {
	e := newEnv(t)
	status, _ := e.do(http.MethodPost, "/v1/payments", map[string]any{"transaction_amount": 50, "payment_method_id": "pix"}, nil)
	assert.Equal(t, http.StatusUnauthorized, status)

	status, pay := e.do(http.MethodPost, "/v1/payments", map[string]any{"transaction_amount": 120.9, "payment_method_id": "pix",
		"external_reference": "pedido-1", "payer": map[string]any{"email": "ana@exemplo.com"}}, auth("k1"))
	require.Equal(t, http.StatusCreated, status)
	assert.Equal(t, "pending", pay["status"])
	assert.InDelta(t, 120.9, pay["transaction_amount"], 0.001)
	id := strconv.FormatInt(int64(pay["id"].(float64)), 10)

	status, _ = e.do(http.MethodPost, "/sim/payments/"+id+"/pay", nil, nil)
	require.Equal(t, http.StatusOK, status)
	_, pay = e.do(http.MethodGet, "/v1/payments/"+id, nil, auth(""))
	assert.Equal(t, "approved", pay["status"])

	status, refund := e.do(http.MethodPost, "/v1/payments/"+id+"/refunds", map[string]any{"amount": 20}, auth("r1"))
	require.Equal(t, http.StatusCreated, status)
	assert.InDelta(t, 20.0, refund["amount"], 0.001)

	req := httptest.NewRequestWithContext(t.Context(), http.MethodGet, "/sim/pix/"+id, nil)
	rec := httptest.NewRecorder()
	e.router.ServeHTTP(rec, req)
	assert.Contains(t, rec.Body.String(), "pago")
}

func TestCardFlow_OverHTTP(t *testing.T) {
	e := newEnv(t)
	card := map[string]any{"card_number": "4235647728025682", "security_code": "123", "expiration_month": 12,
		"expiration_year": time.Now().Year() + 2, "cardholder": map[string]any{"name": "APRO"}}
	status, _ := e.do(http.MethodPost, "/v1/card_tokens?public_key=errada", card, nil)
	assert.Equal(t, http.StatusUnauthorized, status)
	status, tok := e.do(http.MethodPost, "/v1/card_tokens?public_key=pub", card, nil)
	require.Equal(t, http.StatusCreated, status)
	assert.Equal(t, "5682", tok["last_four_digits"])

	status, pay := e.do(http.MethodPost, "/v1/payments", map[string]any{"transaction_amount": 99.9, "payment_method_id": "visa",
		"token": tok["id"], "installments": 3, "capture": false}, auth("k"))
	require.Equal(t, http.StatusCreated, status)
	assert.Equal(t, "authorized", pay["status"])
	id := strconv.FormatInt(int64(pay["id"].(float64)), 10)

	status, pay = e.do(http.MethodPut, "/v1/payments/"+id, map[string]any{"capture": true}, auth(""))
	require.Equal(t, http.StatusOK, status)
	assert.Equal(t, true, pay["captured"])

	status, errBody := e.do(http.MethodPut, "/v1/payments/"+id, map[string]any{"status": "cancelled"}, auth(""))
	assert.Equal(t, http.StatusConflict, status, "capturado não se cancela: estorna")
	assert.Equal(t, "invalid_status", errBody["error"])

	status, _ = e.do(http.MethodGet, "/v1/payments/999", nil, auth(""))
	assert.Equal(t, http.StatusNotFound, status)
}
