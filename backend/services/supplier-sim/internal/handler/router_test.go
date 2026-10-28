package handler

import (
	"bytes"
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/catalog"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/order"
)

type env struct {
	t      *testing.T
	router http.Handler
	orders *order.Simulator
	token  string
}

// newEnv sobe a API com atrasos de milissegundos e sem webhook (o webhook tem teste próprio).
func newEnv(t *testing.T, delays order.Delays, outOfStock ...string) *env {
	t.Helper()
	oos := map[string]bool{}
	for _, v := range outOfStock {
		oos[v] = true
	}
	stock := catalog.NewStock(oos)
	orders := order.NewSimulator(stock, delays, nil)
	t.Cleanup(orders.Close)
	e := &env{t: t, orders: orders,
		router: NewRouter(NewTokens("sim@comprafacil.local", "sim", time.Hour), stock, orders, slog.New(slog.DiscardHandler))}
	rec, body := e.do(http.MethodPost, "/api2.0/v1/authentication/getAccessToken", map[string]string{"email": "sim@comprafacil.local", "password": "sim"}, false)
	require.Equal(t, http.StatusOK, rec.Code)
	var data tokenResponse
	decodeData(t, body, &data)
	e.token = data.AccessToken
	return e
}

func (e *env) do(method, path string, payload any, auth bool) (*httptest.ResponseRecorder, Envelope) {
	e.t.Helper()
	var r io.Reader
	if payload != nil {
		b, err := json.Marshal(payload)
		require.NoError(e.t, err)
		r = bytes.NewReader(b)
	}
	req := httptest.NewRequestWithContext(e.t.Context(), method, path, r)
	req.Header.Set("Content-Type", "application/json")
	if auth {
		req.Header.Set(TokenHeader, e.token)
	}
	rec := httptest.NewRecorder()
	e.router.ServeHTTP(rec, req)
	var env Envelope
	if rec.Body.Len() > 0 {
		require.NoError(e.t, json.Unmarshal(rec.Body.Bytes(), &env), rec.Body.String())
	}
	return rec, env
}

func decodeData(t *testing.T, env Envelope, dst any) {
	t.Helper()
	b, err := json.Marshal(env.Data)
	require.NoError(t, err)
	require.NoError(t, json.Unmarshal(b, dst))
}

func createBody(number, remark string, items ...any) map[string]any {
	products := []map[string]any{}
	for i := 0; i+1 < len(items); i += 2 {
		products = append(products, map[string]any{"vid": items[i], "quantity": items[i+1]})
	}
	return map[string]any{
		"orderNumber": number, "shippingCountryCode": "BR", "shippingCustomerName": "Fulano",
		"shippingAddress": "Rua A, 1", "shippingZip": "01001000", "shippingPhone": "11999999999",
		"email": "fulano@example.com", "remark": remark, "products": products,
	}
}

var slow = order.Delays{Accept: time.Hour, Ship: time.Hour, Deliver: time.Hour}

func TestAuth(t *testing.T) {
	e := newEnv(t, slow)

	rec, env := e.do(http.MethodPost, "/api2.0/v1/authentication/getAccessToken", map[string]string{"email": "sim@comprafacil.local", "password": "errada"}, false)
	assert.Equal(t, http.StatusUnauthorized, rec.Code)
	assert.Equal(t, CodeAuth, env.Code)
	assert.False(t, env.Result)

	rec, env = e.do(http.MethodGet, "/api2.0/v1/product/list", nil, false)
	assert.Equal(t, http.StatusUnauthorized, rec.Code)
	assert.Equal(t, CodeAuth, env.Code)
	assert.Equal(t, "Invalid token", env.Message)
	assert.NotEmpty(t, env.RequestID)

	e.token = "inventado"
	rec, _ = e.do(http.MethodGet, "/api2.0/v1/product/list", nil, true)
	assert.Equal(t, http.StatusUnauthorized, rec.Code)

	rec, _ = e.do(http.MethodGet, "/health", nil, false)
	assert.Equal(t, http.StatusOK, rec.Code)
}

func TestTokenExpiry(t *testing.T) {
	e := newEnv(t, slow)
	rec, env := e.do(http.MethodPost, "/api2.0/v1/authentication/getAccessToken", map[string]string{"email": "sim@comprafacil.local", "password": "sim"}, false)
	require.Equal(t, http.StatusOK, rec.Code)
	var tok tokenResponse
	decodeData(t, env, &tok)
	exp, err := time.Parse(time.RFC3339, tok.AccessTokenExpiryDate)
	require.NoError(t, err)
	assert.WithinDuration(t, exp, time.Now().Add(time.Hour), time.Minute)
	assert.NotEmpty(t, tok.RefreshToken)
}

func TestProductsAndStock(t *testing.T) {
	e := newEnv(t, slow, "ESGOTADO")

	_, env := e.do(http.MethodGet, "/api2.0/v1/product/list?pageNum=2&pageSize=20", nil, true)
	assert.Equal(t, CodeSuccess, env.Code)
	var page struct {
		Total int               `json:"total"`
		List  []catalog.Product `json:"list"`
	}
	decodeData(t, env, &page)
	assert.Equal(t, 30, page.Total)
	assert.Len(t, page.List, 10)

	_, env = e.do(http.MethodGet, "/api2.0/v1/product/stock/queryByVid?vid=SKU-DA-LOJA", nil, true)
	var stock []stockEntry
	decodeData(t, env, &stock)
	require.Len(t, stock, 1)
	assert.Equal(t, "BR", stock[0].AreaID)
	assert.GreaterOrEqual(t, stock[0].StorageNum, 5)

	_, env = e.do(http.MethodGet, "/api2.0/v1/product/stock/queryByVid?vid=ESGOTADO", nil, true)
	decodeData(t, env, &stock)
	assert.Zero(t, stock[0].StorageNum)
}

func TestCreateIdempotent(t *testing.T) {
	e := newEnv(t, slow)
	body := createBody("PED-1", "", "SIM-V-001", 2, "SKU-LOJA", 1)

	_, first := e.do(http.MethodPost, "/api2.0/v1/shopping/order/createOrderV2", body, true)
	require.True(t, first.Result, first.Message)
	var a createResponse
	decodeData(t, first, &a)
	assert.Regexp(t, `^CJ\d{10}$`, a.OrderID)
	assert.Equal(t, order.StatusCreated, a.OrderStatus)
	want := catalog.FormatUSD(2*1240 + catalog.UnitCostCents("SKU-LOJA"))
	assert.Equal(t, want, a.OrderAmount)
	assert.Equal(t, want, a.ProductAmount)

	_, second := e.do(http.MethodPost, "/api2.0/v1/shopping/order/createOrderV2", body, true)
	var b createResponse
	decodeData(t, second, &b)
	assert.Equal(t, a.OrderID, b.OrderID)
	assert.Len(t, e.orders.List(), 1)

	_, other := e.do(http.MethodPost, "/api2.0/v1/shopping/order/createOrderV2", createBody("PED-2", "", "SIM-V-001", 1), true)
	var c createResponse
	decodeData(t, other, &c)
	assert.NotEqual(t, a.OrderID, c.OrderID)
}

func TestCreateRejections(t *testing.T) {
	e := newEnv(t, slow, "ESGOTADO")
	cases := []struct {
		name string
		body map[string]any
		code int
		msg  string
	}{
		{"sem estoque", createBody("R-1", "", "SIM-V-001", 1, "ESGOTADO", 1), CodeOutOfStock, "Out of stock: ESGOTADO"},
		{"acima do estoque", createBody("R-2", "", "SIM-V-001", 201), CodeOutOfStock, "Out of stock: SIM-V-001"},
		{"recusar", createBody("R-3", "cliente chato [recusar]", "SIM-V-001", 1), CodeRejected, "Order rejected by supplier"},
		{"sem produtos", createBody("R-4", ""), CodeInvalidParams, "Invalid params"},
		{"sem orderNumber", createBody("", "", "SIM-V-001", 1), CodeInvalidParams, "Invalid params"},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			rec, env := e.do(http.MethodPost, "/api2.0/v1/shopping/order/createOrderV2", tc.body, true)
			assert.Equal(t, http.StatusOK, rec.Code)
			assert.False(t, env.Result)
			assert.Equal(t, tc.code, env.Code)
			assert.Equal(t, tc.msg, env.Message)
			assert.Nil(t, env.Data)
		})
	}
	assert.Empty(t, e.orders.List())
}

func (e *env) create(number, remark string) string {
	e.t.Helper()
	_, env := e.do(http.MethodPost, "/api2.0/v1/shopping/order/createOrderV2", createBody(number, remark, "SIM-V-005", 1), true)
	require.True(e.t, env.Result, env.Message)
	var r createResponse
	decodeData(e.t, env, &r)
	return r.OrderID
}

func (e *env) detail(id string) (Envelope, order.Order) {
	e.t.Helper()
	_, env := e.do(http.MethodGet, "/api2.0/v1/shopping/order/getOrderDetail?orderId="+id, nil, true)
	var o order.Order
	if env.Result {
		decodeData(e.t, env, &o)
	}
	return env, o
}

func (e *env) waitStatus(id, status string) order.Order {
	e.t.Helper()
	var last order.Order
	require.Eventually(e.t, func() bool {
		_, last = e.detail(id)
		return last.OrderStatus == status
	}, 2*time.Second, 5*time.Millisecond, "esperava %s", status)
	return last
}

func TestLifecycleDelivered(t *testing.T) {
	e := newEnv(t, order.Delays{Accept: 10 * time.Millisecond, Ship: 10 * time.Millisecond, Deliver: 10 * time.Millisecond})
	id := e.create("L-1", "")

	o := e.waitStatus(id, order.StatusDelivered)
	assert.Regexp(t, `^BR\d{9}CJ$`, o.TrackNumber)
	assert.Equal(t, "Correios", o.TrackingProvider)
	assert.Equal(t, "https://t.17track.net/en#nums="+o.TrackNumber, o.TrackingURL)
	assert.Equal(t, 1, o.IsSandbox)
	assert.Nil(t, o.DisputeID)
	assert.Equal(t, "L-1", o.OrderNumber)

	// repetir a criação devolve o mesmo pedido com o status atual
	_, env := e.do(http.MethodPost, "/api2.0/v1/shopping/order/createOrderV2", createBody("L-1", "", "SIM-V-005", 1), true)
	var r createResponse
	decodeData(t, env, &r)
	assert.Equal(t, id, r.OrderID)
	assert.Equal(t, order.StatusDelivered, r.OrderStatus)
}

func TestLifecycleRejectLaterAndStall(t *testing.T) {
	e := newEnv(t, order.Delays{Accept: 10 * time.Millisecond, Ship: 10 * time.Millisecond, Deliver: 10 * time.Millisecond})
	rejected := e.create("L-2", "[recusar-depois]")
	stalled := e.create("L-3", "[atrasar]")

	o := e.waitStatus(rejected, order.StatusCancelled)
	assert.Equal(t, order.ReasonSupplierCancelled, o.Reason)

	o = e.waitStatus(stalled, order.StatusUnshipped)
	assert.Equal(t, "PROCESSING", o.SubStatus)
	time.Sleep(60 * time.Millisecond) // bem depois do ship + deliver: continua parado
	_, o = e.detail(stalled)
	assert.Equal(t, order.StatusUnshipped, o.OrderStatus)
	_, o = e.detail(rejected)
	assert.Equal(t, order.StatusCancelled, o.OrderStatus)
}

func TestDeleteBeforeAndAfterShip(t *testing.T) {
	e := newEnv(t, order.Delays{Accept: 5 * time.Millisecond, Ship: 5 * time.Millisecond, Deliver: time.Hour})

	// antes do accept: CREATED → CANCELLED, e o ciclo não avança mais
	slowEnv := newEnv(t, slow)
	early := slowEnv.create("D-1", "")
	_, env := slowEnv.do(http.MethodDelete, "/api2.0/v1/shopping/order/deleteOrder?orderId="+early, nil, true)
	require.True(t, env.Result, env.Message)
	_, o := slowEnv.detail(early)
	assert.Equal(t, order.StatusCancelled, o.OrderStatus)
	assert.Equal(t, order.ReasonStoreCancelled, o.Reason)
	_, env = slowEnv.do(http.MethodDelete, "/api2.0/v1/shopping/order/deleteOrder?orderId="+early, nil, true)
	assert.True(t, env.Result, "cancelar de novo é idempotente")

	shipped := e.create("D-2", "")
	e.waitStatus(shipped, order.StatusShipped)
	_, env = e.do(http.MethodDelete, "/api2.0/v1/shopping/order/deleteOrder?orderId="+shipped, nil, true)
	assert.False(t, env.Result)
	assert.Equal(t, CodeShipped, env.Code)
	assert.Equal(t, "Order already shipped", env.Message)

	stalled := e.create("D-3", "[atrasar]")
	e.waitStatus(stalled, order.StatusUnshipped)
	_, env = e.do(http.MethodDelete, "/api2.0/v1/shopping/order/deleteOrder?orderId="+stalled, nil, true)
	assert.True(t, env.Result, env.Message)
}

func TestOrderNotFound(t *testing.T) {
	e := newEnv(t, slow)
	env, _ := e.detail("CJ0000000000")
	assert.False(t, env.Result)
	assert.Equal(t, CodeNotFound, env.Code)
	assert.Equal(t, "Order not found", env.Message)

	_, env = e.do(http.MethodDelete, "/api2.0/v1/shopping/order/deleteOrder?orderId=nada", nil, true)
	assert.Equal(t, CodeNotFound, env.Code)
}
