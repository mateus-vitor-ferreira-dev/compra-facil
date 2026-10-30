package order

import (
	"encoding/json"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"slices"
	"sync"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/catalog"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/webhook"
)

func req(number, remark string) CreateRequest {
	r := CreateRequest{OrderNumber: number, Remark: remark, Shipping: Shipping{
		ShippingCountryCode: "BR", ShippingCustomerName: "Ana Souza", ShippingAddress: "Avenida Paulista, 1000",
		ShippingZip: "01310100", ShippingPhone: "11987654321", ConsigneeID: "52998224725",
	}}
	r.Products = append(r.Products, struct {
		VID      string `json:"vid"`
		Quantity int    `json:"quantity"`
	}{"SIM-V-001", 1})
	return r
}

// TestWebhookSequence liga simulador e dispatcher de verdade a um httptest e confere a
// sequência de status recebida para cada caso de ciclo de vida.
func TestWebhookSequence(t *testing.T) {
	var mu sync.Mutex
	seq := map[string][]string{}
	var failed bool
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		var m webhook.OrderMessage
		if err := json.NewDecoder(r.Body).Decode(&m); err != nil {
			w.WriteHeader(http.StatusBadRequest)
			return
		}
		mu.Lock()
		defer mu.Unlock()
		if !failed { // a primeira entrega de todas falha, para forçar um retry no meio
			failed = true
			w.WriteHeader(http.StatusInternalServerError)
			return
		}
		seq[m.Params.OrderNumber] = append(seq[m.Params.OrderNumber], m.Params.OrderStatus)
	}))
	defer srv.Close()

	hooks := webhook.NewDispatcher(webhook.Options{URL: srv.URL, Secret: "s", RetryBase: 5 * time.Millisecond}, slog.New(slog.DiscardHandler))
	sim := NewSimulator(catalog.NewStock(nil), Delays{Accept: 2 * time.Millisecond, Ship: time.Millisecond, Deliver: time.Millisecond}, hooks)

	for _, r := range []CreateRequest{req("ok", ""), req("depois", MarkRejectLater), req("atrasa", MarkStall)} {
		_, err := sim.Create(r)
		require.NoError(t, err)
	}
	require.Eventually(t, func() bool {
		mu.Lock()
		defer mu.Unlock()
		return len(seq["ok"]) == 3 && len(seq["depois"]) == 1 && len(seq["atrasa"]) == 1
	}, 2*time.Second, 5*time.Millisecond)
	sim.Close()
	hooks.Close()

	assert.Equal(t, []string{StatusUnshipped, StatusShipped, StatusDelivered}, seq["ok"])
	assert.Equal(t, []string{StatusCancelled}, seq["depois"])
	assert.Equal(t, []string{StatusUnshipped}, seq["atrasa"])
}

func TestCancelEmitsWebhookAndStopsLifecycle(t *testing.T) {
	n := &recorder{}
	sim := NewSimulator(catalog.NewStock(nil), Delays{Accept: 20 * time.Millisecond, Ship: time.Millisecond, Deliver: time.Millisecond}, n)
	defer sim.Close()

	o, err := sim.Create(req("c", ""))
	require.NoError(t, err)
	_, err = sim.Cancel(o.OrderID)
	require.NoError(t, err)
	time.Sleep(50 * time.Millisecond)

	got, err := sim.Get(o.OrderID)
	require.NoError(t, err)
	assert.Equal(t, StatusCancelled, got.OrderStatus)
	assert.Equal(t, []string{StatusCancelled}, n.statuses())
}

type recorder struct {
	mu       sync.Mutex
	got      []webhook.Params
	disputes []webhook.DisputeParams
}

func (r *recorder) Enqueue(p webhook.Params) {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.got = append(r.got, p)
}

func (r *recorder) EnqueueDispute(p webhook.DisputeParams) {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.disputes = append(r.disputes, p)
}

func (r *recorder) statuses() []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	out := []string{}
	for _, p := range r.got {
		out = append(out, p.OrderStatus)
	}
	return out
}

func delivered(t *testing.T, sim *Simulator, r *recorder, number, remark string) {
	t.Helper()
	_, err := sim.Create(req(number, remark))
	require.NoError(t, err)
	require.Eventually(t, func() bool {
		return slices.Contains(r.statuses(), StatusDelivered)
	}, time.Second, time.Millisecond)
}

func TestOpenDispute_OnlyDelivered_OncePerOrder_AndTheSupplierAnswers(t *testing.T) {
	r := &recorder{}
	sim := NewSimulator(catalog.NewStock(nil), Delays{}, r)
	t.Cleanup(sim.Close)

	_, err := sim.OpenDispute("nao-existe")
	require.ErrorIs(t, err, ErrNotFound)

	delivered(t, sim, r, "PED-OK", "")
	first, err := sim.OpenDispute("PED-OK")
	require.NoError(t, err)
	again, err := sim.OpenDispute("PED-OK")
	require.NoError(t, err)
	assert.Equal(t, first.DisputeID, again.DisputeID)
	assert.NotEmpty(t, first.ReturnCode)
	assert.NotEmpty(t, first.ReturnAddress)

	require.Eventually(t, func() bool {
		r.mu.Lock()
		defer r.mu.Unlock()
		return len(r.disputes) == 1
	}, time.Second, time.Millisecond)
	assert.Equal(t, DisputeApproved, r.disputes[0].DisputeStatus)
	assert.Equal(t, "PED-OK", r.disputes[0].OrderNumber)
}

func TestOpenDispute_RefusedBeforeDelivery_AndRejectedByTheMark(t *testing.T) {
	r := &recorder{}
	sim := NewSimulator(catalog.NewStock(nil), Delays{Accept: time.Hour}, r)
	t.Cleanup(sim.Close)
	_, err := sim.Create(req("PED-CEDO", ""))
	require.NoError(t, err)
	_, err = sim.OpenDispute("PED-CEDO")
	require.ErrorIs(t, err, ErrNotDelivered)

	r2 := &recorder{}
	sim2 := NewSimulator(catalog.NewStock(nil), Delays{}, r2)
	t.Cleanup(sim2.Close)
	delivered(t, sim2, r2, "PED-NAO", MarkRejectReturn)
	_, err = sim2.OpenDispute("PED-NAO")
	require.NoError(t, err)
	require.Eventually(t, func() bool {
		r2.mu.Lock()
		defer r2.mu.Unlock()
		return len(r2.disputes) == 1
	}, time.Second, time.Millisecond)
	assert.Equal(t, DisputeRejected, r2.disputes[0].DisputeStatus)
	assert.NotEmpty(t, r2.disputes[0].Reason)
}
