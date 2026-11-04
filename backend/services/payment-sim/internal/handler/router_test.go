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
	return &env{t: t, router: NewRouter(p, Credentials{AccessToken: "tok", PublicKey: "pub", AdminToken: "adm"}, slog.New(slog.DiscardHandler))}
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

// payer é o pagador que o Mercado Pago exige no PIX: com o documento.
var payer = map[string]any{"email": "ana@exemplo.com", "first_name": "Ana",
	"identification": map[string]any{"type": "CPF", "number": "52998224725"}}

func TestPIX_WithoutDocument_IsRejected(t *testing.T) {
	e := newEnv(t)
	for _, p := range []map[string]any{
		{"email": "ana@exemplo.com"},
		{"email": "ana@exemplo.com", "identification": map[string]any{"type": "CPF", "number": "529.982.247-25"}},
		{"email": "ana@exemplo.com", "identification": map[string]any{"type": "RG", "number": "12345678901"}},
	} {
		status, _ := e.do(http.MethodPost, "/v1/payments", map[string]any{"transaction_amount": 50, "payment_method_id": "pix", "payer": p}, auth("sem-doc"))
		assert.Equal(t, http.StatusBadRequest, status, p)
	}
	// com o documento, a mesma chave cria (a recusa não gastou a chave)
	status, _ := e.do(http.MethodPost, "/v1/payments", map[string]any{"transaction_amount": 50, "payment_method_id": "pix", "payer": payer}, auth("sem-doc"))
	assert.Equal(t, http.StatusCreated, status)
}

func TestPIXFlow_OverHTTP(t *testing.T) {
	e := newEnv(t)
	status, _ := e.do(http.MethodPost, "/v1/payments", map[string]any{"transaction_amount": 50, "payment_method_id": "pix"}, nil)
	assert.Equal(t, http.StatusUnauthorized, status)

	status, pay := e.do(http.MethodPost, "/v1/payments", map[string]any{"transaction_amount": 120.9, "payment_method_id": "pix",
		"external_reference": "pedido-1", "payer": payer}, auth("k1"))
	require.Equal(t, http.StatusCreated, status)
	assert.Equal(t, "pending", pay["status"])
	assert.InDelta(t, 120.9, pay["transaction_amount"], 0.001)
	id := strconv.FormatInt(int64(pay["id"].(float64)), 10)

	// pagar pela API é operação: sem o token de administração, 401 (#280)
	status, _ = e.do(http.MethodPost, "/sim/payments/"+id+"/pay", nil, nil)
	require.Equal(t, http.StatusUnauthorized, status)
	status, _ = e.do(http.MethodPost, "/sim/payments/"+id+"/pay", nil, map[string]string{"X-Sim-Admin-Token": "adm"})
	require.Equal(t, http.StatusOK, status)
	_, pay = e.do(http.MethodGet, "/v1/payments/"+id, nil, auth(""))
	assert.Equal(t, "approved", pay["status"])

	status, refund := e.do(http.MethodPost, "/v1/payments/"+id+"/refunds", map[string]any{"amount": 20}, auth("r1"))
	require.Equal(t, http.StatusCreated, status)
	assert.InDelta(t, 20.0, refund["amount"], 0.001)

	// a página do PIX abre pelo ticket_url (código aleatório), e não pelo id
	ticket := pay["point_of_interaction"].(map[string]any)["transaction_data"].(map[string]any)["ticket_url"].(string)
	page := func(path string) *httptest.ResponseRecorder {
		rec := httptest.NewRecorder()
		e.router.ServeHTTP(rec, httptest.NewRequestWithContext(t.Context(), http.MethodGet, path, nil))
		return rec
	}
	assert.Equal(t, http.StatusNotFound, page("/sim/pix/"+id).Code)
	path := ticket[len("http://localhost:8092"):]
	assert.Regexp(t, `^/sim/pix/[0-9a-f]{32}$`, path)
	assert.Contains(t, page(path).Body.String(), "pago")
}

func TestPIXPage_PaysByTheTicketCode_AndAnIdPaysNothing(t *testing.T) {
	e := newEnv(t)
	_, pay := e.do(http.MethodPost, "/v1/payments", map[string]any{"transaction_amount": 50, "payment_method_id": "pix", "payer": payer}, auth("k2"))
	id := strconv.FormatInt(int64(pay["id"].(float64)), 10)
	ticket := pay["point_of_interaction"].(map[string]any)["transaction_data"].(map[string]any)["ticket_url"].(string)
	post := func(path string) int {
		rec := httptest.NewRecorder()
		e.router.ServeHTTP(rec, httptest.NewRequestWithContext(t.Context(), http.MethodPost, path, nil))
		return rec.Code
	}

	assert.Equal(t, http.StatusNotFound, post("/sim/pix/"+id)) // o id sequencial não paga
	_, still := e.do(http.MethodGet, "/v1/payments/"+id, nil, auth(""))
	assert.Equal(t, "pending", still["status"])

	assert.Equal(t, http.StatusSeeOther, post(ticket[len("http://localhost:8092"):]))
	_, paid := e.do(http.MethodGet, "/v1/payments/"+id, nil, auth(""))
	assert.Equal(t, "approved", paid["status"])
}

func TestOperationRoutes_NeedTheAdminToken(t *testing.T) {
	e := newEnv(t)
	for _, r := range []struct{ method, path string }{{http.MethodGet, "/sim/payments"}, {http.MethodPost, "/sim/reset"}} {
		status, _ := e.do(r.method, r.path, nil, nil)
		assert.Equal(t, http.StatusUnauthorized, status, r.path)
		status, _ = e.do(r.method, r.path, nil, map[string]string{"X-Sim-Admin-Token": "errado"})
		assert.Equal(t, http.StatusUnauthorized, status, r.path)
		status, _ = e.do(r.method, r.path, nil, map[string]string{"X-Sim-Admin-Token": "adm"})
		assert.Contains(t, []int{http.StatusOK, http.StatusNoContent}, status, r.path)
	}

	// sem token configurado, as rotas de operação ficam fechadas para todos
	p := payment.NewProvider(payment.Options{}, nil)
	t.Cleanup(p.Close)
	closed := &env{t: t, router: NewRouter(p, Credentials{AccessToken: "tok", PublicKey: "pub"}, slog.New(slog.DiscardHandler))}
	status, _ := closed.do(http.MethodGet, "/sim/payments", nil, map[string]string{"X-Sim-Admin-Token": ""})
	assert.Equal(t, http.StatusUnauthorized, status)
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

func TestCardTokens_OpenToTheBrowser(t *testing.T) {
	e := newEnv(t)
	req := httptest.NewRequestWithContext(t.Context(), http.MethodOptions, "/v1/card_tokens?public_key=pub", nil)
	req.Header.Set("Origin", "http://localhost:3000")
	req.Header.Set("Access-Control-Request-Method", "POST")
	req.Header.Set("Access-Control-Request-Headers", "content-type")
	rec := httptest.NewRecorder()
	e.router.ServeHTTP(rec, req)
	assert.Equal(t, http.StatusNoContent, rec.Code)
	assert.Equal(t, "*", rec.Header().Get("Access-Control-Allow-Origin"))
	assert.Contains(t, rec.Header().Get("Access-Control-Allow-Headers"), "Content-Type")

	// a resposta também leva o cabeçalho, senão o navegador não entrega o token à página
	req = httptest.NewRequestWithContext(t.Context(), http.MethodPost, "/v1/card_tokens?public_key=pub",
		bytes.NewReader([]byte(`{"card_number":"4509953566233704","security_code":"123","expiration_month":11,"expiration_year":2099,"cardholder":{"name":"APRO"}}`)))
	req.Header.Set("Content-Type", "application/json")
	rec = httptest.NewRecorder()
	e.router.ServeHTTP(rec, req)
	assert.Equal(t, http.StatusCreated, rec.Code)
	assert.Equal(t, "*", rec.Header().Get("Access-Control-Allow-Origin"))

	// as rotas do servidor da loja continuam fechadas ao navegador
	status, _ := e.do(http.MethodOptions, "/v1/payments", nil, nil)
	assert.NotEqual(t, http.StatusNoContent, status)
}
