package handler

import (
	"bytes"
	"encoding/json"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/invoice-sim/internal/nfe"
)

type env struct {
	t      *testing.T
	router http.Handler
	token  string
}

func newEnv(t *testing.T) *env {
	t.Helper()
	r := NewRouter(nfe.NewIssuer(nfe.Options{}), Credentials{ClientID: "cli", ClientSecret: "seg", TokenTTL: time.Hour}, slog.New(slog.DiscardHandler))
	return &env{t: t, router: r}
}

func (e *env) login(id, secret string) (int, map[string]any) {
	e.t.Helper()
	form := url.Values{"grant_type": {"client_credentials"}, "client_id": {id}, "client_secret": {secret}, "scope": {"nfe"}}
	req := httptest.NewRequestWithContext(e.t.Context(), http.MethodPost, "/oauth/token", strings.NewReader(form.Encode()))
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
	rec := httptest.NewRecorder()
	e.router.ServeHTTP(rec, req)
	var out map[string]any
	_ = json.Unmarshal(rec.Body.Bytes(), &out)
	if tok, ok := out["access_token"].(string); ok {
		e.token = tok
	}
	return rec.Code, out
}

func (e *env) do(method, path string, body any) *httptest.ResponseRecorder {
	e.t.Helper()
	var raw []byte
	if body != nil {
		raw, _ = json.Marshal(body)
	}
	req := httptest.NewRequestWithContext(e.t.Context(), method, path, bytes.NewReader(raw))
	req.Header.Set("Content-Type", "application/json")
	if e.token != "" {
		req.Header.Set("Authorization", "Bearer "+e.token)
	}
	rec := httptest.NewRecorder()
	e.router.ServeHTTP(rec, req)
	return rec
}

func decode(t *testing.T, rec *httptest.ResponseRecorder) map[string]any {
	t.Helper()
	var out map[string]any
	require.NoError(t, json.Unmarshal(rec.Body.Bytes(), &out), rec.Body.String())
	return out
}

func emission(ref, email string) map[string]any {
	return map[string]any{
		"ambiente": "homologacao", "referencia": ref,
		"infNFe": map[string]any{
			"versao": "4.00",
			"ide":    map[string]any{"natOp": "Venda", "mod": 55, "serie": 1},
			"emit": map[string]any{"CNPJ": "11222333000181", "xNome": "Loja", "IE": "ISENTO", "CRT": 1,
				"enderEmit": map[string]any{"xLgr": "Rua", "nro": "1", "xBairro": "Centro", "xMun": "Lavras", "UF": "MG", "CEP": "37200000"}},
			"dest": map[string]any{"CPF": "52998224725", "xNome": "Ana Souza", "indIEDest": 9, "email": email,
				"enderDest": map[string]any{"xLgr": "Av. Paulista", "nro": "1000", "xBairro": "Bela Vista", "xMun": "São Paulo", "UF": "SP", "CEP": "01310100"}},
			"det": []map[string]any{{"nItem": 1, "prod": map[string]any{
				"cProd": "MOU-1", "cEAN": "SEM GTIN", "xProd": "Mouse", "NCM": "84716053", "CFOP": "6120", "uCom": "UN",
				"qCom": 1, "vUnCom": 99.9, "vProd": 99.9}}},
			"total": map[string]any{"ICMSTot": map[string]any{"vProd": 99.9, "vNF": 99.9}},
			"pag":   map[string]any{"detPag": []map[string]any{{"tPag": "17", "vPag": 99.9}}},
		},
	}
}

func TestToken_ClientCredentials(t *testing.T) {
	e := newEnv(t)
	assert.Equal(t, http.StatusUnauthorized, e.do(http.MethodGet, "/nfe?referencia=x", nil).Code)
	status, _ := e.login("cli", "errado")
	assert.Equal(t, http.StatusUnauthorized, status)
	status, body := e.login("cli", "seg")
	require.Equal(t, http.StatusOK, status)
	assert.Equal(t, "bearer", body["token_type"])
	assert.InDelta(t, 3600, body["expires_in"], 0)
	assert.Equal(t, http.StatusOK, e.do(http.MethodGet, "/nfe?referencia=x", nil).Code)
}

func TestInvoice_EmitConsultCancelAndDocuments(t *testing.T) {
	e := newEnv(t)
	e.login("cli", "seg")

	rec := e.do(http.MethodPost, "/nfe", emission("pedido-1", "ana@exemplo.com"))
	require.Equal(t, http.StatusOK, rec.Code)
	inv := decode(t, rec)
	assert.Equal(t, "autorizado", inv["status"])
	id := inv["id"].(string)
	assert.Len(t, inv["chave"], 44)

	found := decode(t, e.do(http.MethodGet, "/nfe?ambiente=homologacao&referencia=pedido-1", nil))
	assert.Len(t, found["data"], 1)
	assert.Equal(t, http.StatusOK, e.do(http.MethodGet, "/nfe/"+id, nil).Code)

	xml := e.do(http.MethodGet, "/nfe/"+id+"/xml", nil)
	assert.Equal(t, "application/xml", xml.Header().Get("Content-Type"))
	assert.Contains(t, xml.Body.String(), inv["chave"].(string))
	pdf := e.do(http.MethodGet, "/nfe/"+id+"/pdf", nil)
	assert.Equal(t, "application/pdf", pdf.Header().Get("Content-Type"))
	assert.True(t, bytes.HasPrefix(pdf.Body.Bytes(), []byte("%PDF")))

	assert.Equal(t, http.StatusBadRequest, e.do(http.MethodPost, "/nfe/"+id+"/cancelamento", map[string]string{"justificativa": "curta"}).Code)
	cancel := e.do(http.MethodPost, "/nfe/"+id+"/cancelamento", map[string]string{"justificativa": "Pedido estornado ao cliente"})
	require.Equal(t, http.StatusOK, cancel.Code)
	assert.Equal(t, "registrado", decode(t, cancel)["status"])
	assert.Equal(t, "cancelado", decode(t, e.do(http.MethodGet, "/nfe/"+id, nil))["status"])
	assert.Equal(t, http.StatusOK, e.do(http.MethodGet, "/nfe/"+id+"/cancelamento", nil).Code)
	assert.Equal(t, http.StatusNotFound, e.do(http.MethodGet, "/nfe/nao-existe", nil).Code)
}

func TestInvoice_RejectedHasNoDocuments(t *testing.T) {
	e := newEnv(t)
	e.login("cli", "seg")
	rec := e.do(http.MethodPost, "/nfe", emission("pedido-2", "ana+recusar-nota@exemplo.com"))
	require.Equal(t, http.StatusOK, rec.Code)
	inv := decode(t, rec)
	assert.Equal(t, "rejeitado", inv["status"])
	id := inv["id"].(string)
	assert.Equal(t, http.StatusNotFound, e.do(http.MethodGet, "/nfe/"+id+"/pdf", nil).Code)
	assert.Equal(t, http.StatusUnprocessableEntity,
		e.do(http.MethodPost, "/nfe/"+id+"/cancelamento", map[string]string{"justificativa": "Pedido estornado ao cliente"}).Code)
	assert.Equal(t, http.StatusBadRequest, e.do(http.MethodPost, "/nfe", map[string]any{"ambiente": "x"}).Code)
}
