package handler

import (
	"bytes"
	"encoding/base64"
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strconv"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/payment-sim/internal/efi"
)

const payee = "0123456789abcdef0123456789abcdef"

// received guarda o que chegou ao "servidor da loja" (os avisos da Efí).
type received struct {
	mu    sync.Mutex
	items []recv
}

type recv struct {
	path, contentType, body string
}

func (r *received) all() []recv {
	r.mu.Lock()
	defer r.mu.Unlock()
	return append([]recv(nil), r.items...)
}

type efiEnv struct {
	*env
	provider *efi.Provider
	hooks    *efi.Dispatcher
	store    *httptest.Server
	got      *received
}

func newEfiEnv(t *testing.T) *efiEnv {
	t.Helper()
	got := &received{}
	store := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		body, _ := io.ReadAll(r.Body)
		got.mu.Lock()
		got.items = append(got.items, recv{path: r.URL.RequestURI(), contentType: r.Header.Get("Content-Type"), body: string(body)})
		got.mu.Unlock()
	}))
	t.Cleanup(store.Close)
	hooks := efi.NewDispatcher(efi.DispatcherOptions{RetryBase: 10 * time.Millisecond}, slog.New(slog.DiscardHandler))
	t.Cleanup(hooks.Close)
	p := efi.NewProvider(efi.Options{ClientID: "id", ClientSecret: "secret", PayeeCode: payee, PixKey: "chave-sim",
		SettleDelay: time.Hour, RefundDelay: 10 * time.Millisecond}, hooks)
	t.Cleanup(p.Close)
	router := NewRouter(p, Credentials{AdminToken: "adm"}, slog.New(slog.DiscardHandler))
	return &efiEnv{env: &env{t: t, router: router}, provider: p, hooks: hooks, store: store, got: got}
}

func (e *efiEnv) token(path string) map[string]string {
	e.t.Helper()
	req := httptest.NewRequestWithContext(e.t.Context(), http.MethodPost, path, strings.NewReader(`{"grant_type":"client_credentials"}`))
	req.Header.Set("Authorization", "Basic "+base64.StdEncoding.EncodeToString([]byte("id:secret")))
	rec := httptest.NewRecorder()
	e.router.ServeHTTP(rec, req)
	require.Equal(e.t, http.StatusOK, rec.Code)
	var out map[string]any
	require.NoError(e.t, json.Unmarshal(rec.Body.Bytes(), &out))
	return map[string]string{"Authorization": "Bearer " + out["access_token"].(string)}
}

func (e *efiEnv) cardToken(number string) string {
	e.t.Helper()
	status, out := e.do(http.MethodPost, "/sim/card-token", map[string]any{"payee_code": payee, "brand": "visa", "number": number,
		"cvv": "123", "expiration_month": "05", "expiration_year": strconv.Itoa(time.Now().Year() + 2), "holder_name": "Ana Souza"}, nil)
	require.Equal(e.t, http.StatusOK, status, out)
	data := out["data"].(map[string]any)
	assert.Equal(e.t, "XXXXXXXXXXXX"+number[len(number)-4:], data["card_mask"])
	return data["payment_token"].(string)
}

var customer = map[string]any{"name": "Ana Souza", "cpf": "52998224725", "email": "ana@exemplo.com", "phone_number": "35999990000"}

// cartões que passam no Luhn: o último dígito decide, como na homologação da Efí
const (
	approvedCard = "4485785674290087"
	refusedCard  = "4111111111111111" // final 1: dados do cartão inválidos
)

func (e *efiEnv) charge(h map[string]string) int64 {
	e.t.Helper()
	status, out := e.do(http.MethodPost, "/v1/charge", map[string]any{
		"items":    []map[string]any{{"name": "Pedido 1", "value": 12990, "amount": 1}},
		"metadata": map[string]any{"custom_id": "pedido-1", "notification_url": e.store.URL + "/v1/webhooks/efi/charges"},
	}, h)
	require.Equal(e.t, http.StatusOK, status, out)
	data := out["data"].(map[string]any)
	assert.Equal(e.t, "new", data["status"])
	assert.InDelta(e.t, 12990, data["total"], 0)
	return int64(data["charge_id"].(float64))
}

func (e *efiEnv) pay(h map[string]string, id int64, token string) (int, map[string]any) {
	return e.do(http.MethodPost, "/v1/charge/"+strconv.FormatInt(id, 10)+"/pay", map[string]any{"payment": map[string]any{
		"credit_card": map[string]any{"customer": customer, "installments": 3, "payment_token": token}}}, h)
}

func TestEfi_Auth_IsRequired_AndBadCredentialsArePlainText(t *testing.T) {
	e := newEfiEnv(t)
	status, _ := e.do(http.MethodGet, "/v1/charge/1", nil, nil)
	assert.Equal(t, http.StatusUnauthorized, status)

	req := httptest.NewRequestWithContext(t.Context(), http.MethodPost, "/v1/authorize", strings.NewReader(`{"grant_type":"client_credentials"}`))
	req.SetBasicAuth("id", "errado")
	rec := httptest.NewRecorder()
	e.router.ServeHTTP(rec, req)
	assert.Equal(t, http.StatusUnauthorized, rec.Code)
	assert.Equal(t, "unauthorized", rec.Body.String())

	// o token da API Pix também abre as rotas (a Efí separa por host; o simulador é um só)
	status, _ = e.do(http.MethodGet, "/v2/cob/naoexiste0000000000000000000", nil, e.token("/oauth/token"))
	assert.Equal(t, http.StatusNotFound, status)
}

func TestEfi_Card_TwoSteps_Approved_ThenPaid_ThenRefunded(t *testing.T) {
	e := newEfiEnv(t)
	h := e.token("/v1/authorize")
	id := e.charge(h)

	status, out := e.pay(h, id, e.cardToken(approvedCard))
	require.Equal(t, http.StatusOK, status, out)
	data := out["data"].(map[string]any)
	assert.Equal(t, "approved", data["status"])
	assert.InDelta(t, 3, data["installments"], 0)
	assert.InDelta(t, 4330, data["installment_value"], 0)

	// pagar de novo (o payments caiu e tentou outra vez) não cobra duas vezes
	status, out = e.pay(h, id, e.cardToken(approvedCard))
	assert.Equal(t, http.StatusBadRequest, status)
	assert.InDelta(t, 3500030, out["code"], 0)

	// aprovado ainda não se estorna nem se cancela
	path := "/v1/charge/" + strconv.FormatInt(id, 10)
	status, _ = e.do(http.MethodPost, "/v1/charge/card/"+strconv.FormatInt(id, 10)+"/refund", nil, h)
	assert.Equal(t, http.StatusBadRequest, status)
	status, out = e.do(http.MethodPut, path+"/cancel", nil, h)
	assert.Equal(t, http.StatusBadRequest, status)
	assert.InDelta(t, 3500043, out["code"], 0)

	status, _ = e.do(http.MethodPost, "/sim/efi/charges/"+strconv.FormatInt(id, 10)+"/settle", nil, map[string]string{"X-Sim-Admin-Token": "adm"})
	require.Equal(t, http.StatusOK, status)
	status, out = e.do(http.MethodGet, path, nil, h)
	require.Equal(t, http.StatusOK, status)
	data = out["data"].(map[string]any)
	assert.Equal(t, "paid", data["status"])
	assert.Equal(t, "pedido-1", data["custom_id"])
	assert.Equal(t, "448578XXXXXX0087", data["payment"].(map[string]any)["credit_card"].(map[string]any)["mask"])

	// parcial: um por dia; depois o resto
	status, out = e.do(http.MethodPost, "/v1/charge/card/"+strconv.FormatInt(id, 10)+"/refund", map[string]any{"amount": 990}, h)
	require.Equal(t, http.StatusOK, status, out)
	assert.Equal(t, "Reembolso em processamento", out["data"])
	require.Eventually(t, func() bool {
		_, out := e.do(http.MethodPost, "/v1/charge/card/"+strconv.FormatInt(id, 10)+"/refund", map[string]any{"amount": 10}, h)
		return strings.Contains(out["error_description"].(string), "um estorno parcial por dia")
	}, time.Second, 10*time.Millisecond)
	status, _ = e.do(http.MethodPost, "/v1/charge/card/"+strconv.FormatInt(id, 10)+"/refund", nil, h)
	require.Equal(t, http.StatusOK, status)
	require.Eventually(t, func() bool {
		_, out := e.do(http.MethodGet, path, nil, h)
		return out["data"].(map[string]any)["status"] == "refunded"
	}, time.Second, 10*time.Millisecond)

	// cada mudança avisou a loja com o mesmo token, e a consulta dá a história inteira
	e.hooks.Wait()
	var tokens []string
	for _, r := range e.got.all() {
		assert.Equal(t, "/v1/webhooks/efi/charges", r.path)
		assert.Equal(t, "application/x-www-form-urlencoded", r.contentType)
		tokens = append(tokens, strings.TrimPrefix(r.body, "notification="))
	}
	require.NotEmpty(t, tokens)
	for _, tok := range tokens {
		assert.Equal(t, tokens[0], tok)
	}
	status, out = e.do(http.MethodGet, "/v1/notification/"+tokens[0], nil, h)
	require.Equal(t, http.StatusOK, status)
	var statuses []string
	for _, ev := range out["data"].([]any) {
		m := ev.(map[string]any)
		statuses = append(statuses, m["status"].(map[string]any)["current"].(string))
		assert.InDelta(t, id, m["identifiers"].(map[string]any)["charge_id"], 0)
		if m["status"].(map[string]any)["current"] == "paid" {
			assert.InDelta(t, 12990, m["value"], 0)
		}
	}
	assert.Equal(t, []string{"new", "waiting", "approved", "paid", "refunded"}, statuses)
}

func TestEfi_Card_RefusedByLastDigit_IsNotAnHTTPError(t *testing.T) {
	e := newEfiEnv(t)
	h := e.token("/v1/authorize")
	id := e.charge(h)
	status, out := e.pay(h, id, e.cardToken(refusedCard))
	require.Equal(t, http.StatusOK, status)
	data := out["data"].(map[string]any)
	assert.Equal(t, "unpaid", data["status"])
	assert.Equal(t, "Dados do cartão inválidos.", data["refusal"].(map[string]any)["reason"])
	status, _ = e.do(http.MethodPut, "/v1/charge/"+strconv.FormatInt(id, 10)+"/cancel", nil, h)
	assert.Equal(t, http.StatusOK, status)
}

func TestEfi_Card_Validation(t *testing.T) {
	e := newEfiEnv(t)
	h := e.token("/v1/authorize")
	id := e.charge(h)
	tok := e.cardToken(approvedCard)
	for _, c := range []map[string]any{
		{"name": "Ana", "cpf": "52998224725", "email": "ana@exemplo.com", "phone_number": "35999990000"},
		{"name": "Ana Souza", "cpf": "529", "email": "ana@exemplo.com", "phone_number": "35999990000"},
		{"name": "Ana Souza", "cpf": "52998224725", "email": "ana@exemplo.com"},
	} {
		status, _ := e.do(http.MethodPost, "/v1/charge/"+strconv.FormatInt(id, 10)+"/pay", map[string]any{"payment": map[string]any{
			"credit_card": map[string]any{"customer": c, "payment_token": tok}}}, h)
		assert.Equal(t, http.StatusBadRequest, status, c)
	}
	// o token só serve uma vez
	_, _ = e.pay(h, id, tok)
	other := e.charge(h)
	status, _ := e.pay(h, other, tok)
	assert.Equal(t, http.StatusBadRequest, status)

	status, _ = e.do(http.MethodPost, "/sim/card-token", map[string]any{"payee_code": "outra-conta", "brand": "visa",
		"number": approvedCard, "cvv": "123", "expiration_month": "05", "expiration_year": "2030"}, nil)
	assert.Equal(t, http.StatusUnauthorized, status)
	status, _ = e.do(http.MethodPost, "/sim/card-token", map[string]any{"payee_code": payee, "brand": "visa",
		"number": "4485785674290088", "cvv": "123", "expiration_month": "05", "expiration_year": "2030"}, nil)
	assert.Equal(t, http.StatusBadRequest, status, "Luhn")
}

func TestEfi_Pix_Cob_Paid_NotifiesWebhook_ThenRefund(t *testing.T) {
	e := newEfiEnv(t)
	h := e.token("/oauth/token")
	status, _ := e.do(http.MethodPut, "/v2/webhook/chave-sim", map[string]any{"webhookUrl": e.store.URL + "/v1/webhooks/efi?hmac=segredo&ignorar="}, h)
	require.Equal(t, http.StatusCreated, status)

	txid := "cf0192f1a2b3c4d5e6f708192a3b4c5d6e"
	body := map[string]any{"calendario": map[string]any{"expiracao": 1800}, "devedor": map[string]any{"cpf": "52998224725", "nome": "Ana Souza"},
		"valor": map[string]any{"original": "129.90"}, "chave": "chave-sim", "solicitacaoPagador": "Pedido 1"}
	status, out := e.do(http.MethodPut, "/v2/cob/"+txid, body, h)
	require.Equal(t, http.StatusCreated, status, out)
	assert.Equal(t, "ATIVA", out["status"])
	assert.InDelta(t, 1800, out["calendario"].(map[string]any)["expiracao"], 0)
	assert.NotEmpty(t, out["pixCopiaECola"])
	loc := out["loc"].(map[string]any)

	status, out = e.do(http.MethodPut, "/v2/cob/"+txid, body, h)
	assert.Equal(t, http.StatusConflict, status)
	assert.Equal(t, "txid_duplicado", out["nome"])

	status, out = e.do(http.MethodGet, "/v2/loc/"+strconv.FormatInt(int64(loc["id"].(float64)), 10)+"/qrcode", nil, h)
	require.Equal(t, http.StatusOK, status)
	assert.True(t, strings.HasPrefix(out["imagemQrcode"].(string), "data:image/svg+xml;base64,"))
	link := out["linkVisualizacao"].(string)
	page := link[strings.LastIndex(link, "/")+1:]

	// o banco paga pela página
	req := httptest.NewRequestWithContext(t.Context(), http.MethodPost, "/sim/cob/pagar/"+page, nil)
	rec := httptest.NewRecorder()
	e.router.ServeHTTP(rec, req)
	require.Equal(t, http.StatusSeeOther, rec.Code)

	status, out = e.do(http.MethodGet, "/v2/cob/"+txid, nil, h)
	require.Equal(t, http.StatusOK, status)
	assert.Equal(t, "CONCLUIDA", out["status"])
	assert.Equal(t, "1800", out["calendario"].(map[string]any)["expiracao"], "na consulta, a validade vem como texto")
	pix := out["pix"].([]any)[0].(map[string]any)
	e2e := pix["endToEndId"].(string)
	assert.Len(t, e2e, 32)

	// devolução: o id é da loja, repetido é 409; R$ 0,01 é recusada
	status, out = e.do(http.MethodPut, "/v2/pix/"+e2e+"/devolucao/dev1", map[string]any{"valor": "29.90"}, h)
	require.Equal(t, http.StatusCreated, status, out)
	assert.Equal(t, "EM_PROCESSAMENTO", out["status"])
	status, _ = e.do(http.MethodPut, "/v2/pix/"+e2e+"/devolucao/dev1", map[string]any{"valor": "29.90"}, h)
	assert.Equal(t, http.StatusConflict, status)
	status, _ = e.do(http.MethodPut, "/v2/pix/"+e2e+"/devolucao/dev2", map[string]any{"valor": "100.01"}, h)
	assert.Equal(t, http.StatusBadRequest, status, "passa do que foi pago")
	require.Eventually(t, func() bool {
		_, out := e.do(http.MethodGet, "/v2/pix/"+e2e+"/devolucao/dev1", nil, h)
		return out["status"] == "DEVOLVIDO"
	}, time.Second, 10*time.Millisecond)

	e.hooks.Wait()
	got := e.got.all()
	require.Len(t, got, 2)
	for _, r := range got {
		assert.Equal(t, "/v1/webhooks/efi?hmac=segredo&ignorar=/pix", r.path)
		var payload struct {
			Pix []efi.PixItem `json:"pix"`
		}
		require.NoError(t, json.NewDecoder(bytes.NewBufferString(r.body)).Decode(&payload))
		assert.Equal(t, txid, payload.Pix[0].Txid)
	}
	assert.Contains(t, got[1].body, `"status":"DEVOLVIDO"`)
}

func TestEfi_Pix_Remove_AndValidation(t *testing.T) {
	e := newEfiEnv(t)
	h := e.token("/oauth/token")
	txid := "cf0192f1a2b3c4d5e6f708192a3b4c5d6f"
	valid := map[string]any{"calendario": map[string]any{"expiracao": 60}, "valor": map[string]any{"original": "10.00"}, "chave": "chave-sim"}
	for name, body := range map[string]map[string]any{
		"chave de outra conta": {"calendario": map[string]any{"expiracao": 60}, "valor": map[string]any{"original": "10.00"}, "chave": "outra"},
		"valor sem centavos":   {"calendario": map[string]any{"expiracao": 60}, "valor": map[string]any{"original": "10"}, "chave": "chave-sim"},
		"CPF curto": {"calendario": map[string]any{"expiracao": 60}, "valor": map[string]any{"original": "10.00"}, "chave": "chave-sim",
			"devedor": map[string]any{"cpf": "123", "nome": "Ana Souza"}},
	} {
		status, _ := e.do(http.MethodPut, "/v2/cob/"+txid, body, h)
		assert.Equal(t, http.StatusBadRequest, status, name)
	}
	status, _ := e.do(http.MethodPut, "/v2/cob/curto", valid, h)
	assert.Equal(t, http.StatusBadRequest, status, "txid curto")

	status, _ = e.do(http.MethodPut, "/v2/cob/"+txid, valid, h)
	require.Equal(t, http.StatusCreated, status)
	status, out := e.do(http.MethodPatch, "/v2/cob/"+txid, map[string]any{"status": "REMOVIDA_PELO_USUARIO_RECEBEDOR"}, h)
	require.Equal(t, http.StatusOK, status)
	assert.Equal(t, "REMOVIDA_PELO_USUARIO_RECEBEDOR", out["status"])
	status, out = e.do(http.MethodPatch, "/v2/cob/"+txid, map[string]any{"status": "REMOVIDA_PELO_USUARIO_RECEBEDOR"}, h)
	assert.Equal(t, http.StatusBadRequest, status)
	assert.Equal(t, "status_cobranca_invalido", out["nome"])
	status, _ = e.do(http.MethodPost, "/sim/efi/cobs/"+txid+"/pay", nil, map[string]string{"X-Sim-Admin-Token": "adm"})
	assert.Equal(t, http.StatusBadRequest, status, "removida não se paga")
}
