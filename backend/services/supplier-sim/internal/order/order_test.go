package order

import (
	"encoding/json"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"sync"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/catalog"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/webhook"
)

func req(number, remark string) CreateRequest {
	r := CreateRequest{OrderNumber: number, Remark: remark}
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
		var m webhook.Message
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
	mu  sync.Mutex
	got []webhook.Params
}

func (r *recorder) Enqueue(p webhook.Params) {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.got = append(r.got, p)
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
