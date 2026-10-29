package webhook

import (
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/payment-sim/internal/payment"
)

func TestDelivers_SignedLikeMercadoPago(t *testing.T) {
	type got struct {
		query, signature, requestID string
		body                        Notification
	}
	received := make(chan got, 2)
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		raw, _ := io.ReadAll(r.Body)
		var n Notification
		_ = json.Unmarshal(raw, &n)
		received <- got{r.URL.RawQuery, r.Header.Get("x-signature"), r.Header.Get("x-request-id"), n}
	}))
	defer srv.Close()
	d := NewDispatcher(Options{URL: srv.URL + "/v1/webhooks/payments", Secret: "s", RetryBase: time.Millisecond}, slog.New(slog.DiscardHandler))
	defer d.Close()

	d.PaymentChanged(payment.Payment{ID: 1000000001, Status: payment.StatusApproved})
	g := <-received

	assert.Equal(t, "payment", g.body.Type)
	assert.Equal(t, "1000000001", g.body.Data.ID)
	assert.Contains(t, g.query, "data.id=1000000001")
	ts, v1, ok := strings.Cut(strings.TrimPrefix(g.signature, "ts="), ",v1=")
	require.True(t, ok, g.signature)
	assert.Equal(t, Sign("1000000001", g.requestID, ts, "s"), v1)
}

func TestRetries_AndUsesTheNotificationURL(t *testing.T) {
	calls := 0
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		calls++
		if calls == 1 {
			w.WriteHeader(http.StatusServiceUnavailable)
		}
	}))
	defer srv.Close()
	d := NewDispatcher(Options{URL: "http://nao-usado.invalid", RetryBase: time.Millisecond}, slog.New(slog.DiscardHandler))
	defer d.Close()

	d.PaymentChanged(payment.Payment{ID: 1, NotificationURL: srv.URL})
	d.Wait()

	assert.Equal(t, 2, calls)
}
