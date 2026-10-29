// Package webhook avisa a loja de cada pagamento que mudou, no formato do Mercado Pago: o corpo só diz
// qual pagamento (quem recebe consulta GET /v1/payments/{id}) e a assinatura vai em x-signature.
package webhook

import (
	"bytes"
	"context"
	"crypto/hmac"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"net/url"
	"strconv"
	"sync"
	"time"

	"github.com/google/uuid"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/payment-sim/internal/payment"
)

// Notification é o corpo da notificação do Mercado Pago (type payment).
type Notification struct {
	ID          int64     `json:"id"`
	LiveMode    bool      `json:"live_mode"`
	Type        string    `json:"type"`
	DateCreated time.Time `json:"date_created"`
	APIVersion  string    `json:"api_version"`
	Action      string    `json:"action"`
	Data        struct {
		ID string `json:"id"`
	} `json:"data"`
}

type Options struct {
	// URL padrão; o notification_url do pagamento, quando há, vence
	URL       string
	Secret    string
	Attempts  int
	RetryBase time.Duration
	Timeout   time.Duration
}

// Dispatcher entrega na ordem de cada pagamento, uma de cada vez, com retry; pagamentos diferentes não
// se bloqueiam.
type Dispatcher struct {
	opts   Options
	client *http.Client
	log    *slog.Logger

	ctx    context.Context
	cancel context.CancelFunc
	wg     sync.WaitGroup

	mu      sync.Mutex
	seq     int64
	queues  map[int64][]delivery
	running map[int64]bool
}

type delivery struct {
	url  string
	body Notification
}

func NewDispatcher(opts Options, log *slog.Logger) *Dispatcher {
	if opts.Attempts <= 0 {
		opts.Attempts = 5
	}
	if opts.Timeout <= 0 {
		opts.Timeout = 5 * time.Second
	}
	ctx, cancel := context.WithCancel(context.Background())
	return &Dispatcher{opts: opts, client: &http.Client{Timeout: opts.Timeout}, log: log, ctx: ctx, cancel: cancel,
		queues: map[int64][]delivery{}, running: map[int64]bool{}}
}

// PaymentChanged enfileira a notificação do pagamento e volta na hora. Sem destino, descarta.
func (d *Dispatcher) PaymentChanged(p payment.Payment) {
	target := d.opts.URL
	if p.NotificationURL != "" {
		target = p.NotificationURL
	}
	if target == "" {
		return
	}
	d.mu.Lock()
	defer d.mu.Unlock()
	if d.ctx.Err() != nil {
		return
	}
	d.seq++
	n := Notification{ID: d.seq, Type: "payment", DateCreated: time.Now().UTC(), APIVersion: "v1", Action: "payment.updated"}
	n.Data.ID = strconv.FormatInt(p.ID, 10)
	d.queues[p.ID] = append(d.queues[p.ID], delivery{url: target, body: n})
	if !d.running[p.ID] {
		d.running[p.ID] = true
		d.wg.Add(1)
		go d.drain(p.ID)
	}
}

// Wait espera as filas esvaziarem (testes); Close interrompe os retries pendentes.
func (d *Dispatcher) Wait() { d.wg.Wait() }

func (d *Dispatcher) Close() {
	d.mu.Lock()
	d.cancel()
	d.mu.Unlock()
	d.wg.Wait()
}

func (d *Dispatcher) drain(id int64) {
	defer d.wg.Done()
	for {
		d.mu.Lock()
		q := d.queues[id]
		if len(q) == 0 {
			delete(d.queues, id)
			delete(d.running, id)
			d.mu.Unlock()
			return
		}
		next := q[0]
		d.queues[id] = q[1:]
		d.mu.Unlock()
		d.deliver(next)
	}
}

func (d *Dispatcher) deliver(x delivery) {
	body, err := json.Marshal(x.body)
	if err != nil {
		d.log.Error("webhook: falha ao serializar", "error", err.Error())
		return
	}
	log := d.log.With("payment_id", x.body.Data.ID, "notification_id", x.body.ID)
	wait := d.opts.RetryBase
	for attempt := 1; attempt <= d.opts.Attempts; attempt++ {
		err := d.post(x, body)
		if err == nil {
			log.Info("webhook entregue", "attempt", attempt)
			return
		}
		if attempt == d.opts.Attempts {
			log.Error("webhook descartado após esgotar as tentativas", "attempts", attempt, "error", err.Error())
			return
		}
		log.Warn("webhook falhou, nova tentativa agendada", "attempt", attempt, "retry_in", wait.String(), "error", err.Error())
		select {
		case <-d.ctx.Done():
			return
		case <-time.After(wait):
		}
		wait *= 2
	}
}

// post manda como o Mercado Pago: ?data.id=&type= na URL, x-request-id e x-signature nos headers.
func (d *Dispatcher) post(x delivery, body []byte) error {
	u, err := url.Parse(x.url)
	if err != nil {
		return fmt.Errorf("URL: %w", err)
	}
	q := u.Query()
	q.Set("data.id", x.body.Data.ID)
	q.Set("type", "payment")
	u.RawQuery = q.Encode()
	req, err := http.NewRequestWithContext(d.ctx, http.MethodPost, u.String(), bytes.NewReader(body))
	if err != nil {
		return fmt.Errorf("requisição: %w", err)
	}
	requestID := uuid.NewString()
	ts := strconv.FormatInt(time.Now().UnixMilli(), 10)
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("x-request-id", requestID)
	req.Header.Set("x-signature", "ts="+ts+",v1="+Sign(x.body.Data.ID, requestID, ts, d.opts.Secret))
	resp, err := d.client.Do(req)
	if err != nil {
		return fmt.Errorf("envio: %w", err)
	}
	defer resp.Body.Close()
	_, _ = io.Copy(io.Discard, resp.Body)
	if resp.StatusCode < 200 || resp.StatusCode > 299 {
		return fmt.Errorf("destino respondeu %d", resp.StatusCode)
	}
	return nil
}

// Sign é o v1 do x-signature do Mercado Pago: HMAC-SHA256 (hex) do manifesto id:<data.id>;request-id:<x-request-id>;ts:<ts>;
func Sign(dataID, requestID, ts, secret string) string {
	m := hmac.New(sha256.New, []byte(secret))
	_, _ = m.Write([]byte("id:" + dataID + ";request-id:" + requestID + ";ts:" + ts + ";"))
	return hex.EncodeToString(m.Sum(nil))
}
