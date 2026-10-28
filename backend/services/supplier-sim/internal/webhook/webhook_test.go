package webhook

import (
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

type received struct {
	msg       Message
	signature string
	body      []byte
}

// receiver grava o que chega; fail decide, por chamada, se responde 500.
type receiver struct {
	mu   sync.Mutex
	got  []received
	fail func(n int, m Message) bool
	n    atomic.Int32
}

func (r *receiver) ServeHTTP(w http.ResponseWriter, req *http.Request) {
	body, _ := io.ReadAll(req.Body)
	var m Message
	_ = json.Unmarshal(body, &m)
	n := int(r.n.Add(1))
	if r.fail != nil && r.fail(n, m) {
		w.WriteHeader(http.StatusInternalServerError)
		return
	}
	r.mu.Lock()
	r.got = append(r.got, received{msg: m, signature: req.Header.Get(SignatureHeader), body: body})
	r.mu.Unlock()
	w.WriteHeader(http.StatusNoContent)
}

func newDispatcher(t *testing.T, url string, dup bool) *Dispatcher {
	t.Helper()
	d := NewDispatcher(Options{URL: url, Secret: "segredo", Attempts: 5, RetryBase: time.Millisecond, Duplicate: dup}, slog.New(slog.DiscardHandler))
	t.Cleanup(d.Close)
	return d
}

func TestDeliverSignedRetriedAndOrdered(t *testing.T) {
	// a primeira tentativa de UNSHIPPED falha: SHIPPED tem de esperar o retry
	var failedOnce atomic.Bool
	rcv := &receiver{fail: func(_ int, m Message) bool {
		return m.Params.OrderStatus == "UNSHIPPED" && failedOnce.CompareAndSwap(false, true)
	}}
	srv := httptest.NewServer(rcv)
	defer srv.Close()

	d := newDispatcher(t, srv.URL, false)
	for _, st := range []string{"UNSHIPPED", "SHIPPED", "DELIVERED"} {
		d.Enqueue(Params{OrderID: "CJ1", OrderNumber: "PED-1", OrderStatus: st})
	}
	d.Wait()

	require.Len(t, rcv.got, 3)
	assert.Equal(t, int32(4), rcv.n.Load(), "3 entregas + 1 retry")
	for i, st := range []string{"UNSHIPPED", "SHIPPED", "DELIVERED"} {
		g := rcv.got[i]
		assert.Equal(t, st, g.msg.Params.OrderStatus)
		assert.Equal(t, "ORDER", g.msg.Type)
		assert.Equal(t, "UPDATE", g.msg.MessageType)
		assert.NotEmpty(t, g.msg.MessageID)
		assert.Equal(t, "sha256="+Sign(g.body, "segredo"), g.signature)
	}
}

func TestRetryKeepsMessageIDAndGivesUp(t *testing.T) {
	var ids []string
	var mu sync.Mutex
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		var m Message
		_ = json.NewDecoder(r.Body).Decode(&m)
		mu.Lock()
		ids = append(ids, m.MessageID)
		mu.Unlock()
		w.WriteHeader(http.StatusBadGateway)
	}))
	defer srv.Close()

	d := newDispatcher(t, srv.URL, false)
	d.Enqueue(Params{OrderID: "CJ1", OrderStatus: "UNSHIPPED"})
	d.Wait()

	require.Len(t, ids, 5)
	for _, id := range ids {
		assert.Equal(t, ids[0], id)
	}
}

func TestDuplicate(t *testing.T) {
	rcv := &receiver{}
	srv := httptest.NewServer(rcv)
	defer srv.Close()

	d := newDispatcher(t, srv.URL, true)
	d.Enqueue(Params{OrderID: "CJ1", OrderStatus: "UNSHIPPED"})
	d.Wait()

	require.Len(t, rcv.got, 2)
	assert.Equal(t, rcv.got[0].msg.MessageID, rcv.got[1].msg.MessageID)
}

func TestNoURLDropsSilently(t *testing.T) {
	d := newDispatcher(t, "", false)
	d.Enqueue(Params{OrderID: "CJ1", OrderStatus: "UNSHIPPED"})
	d.Wait()
}
