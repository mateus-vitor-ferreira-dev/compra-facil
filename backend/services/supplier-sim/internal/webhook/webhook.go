// Package webhook entrega as mudanças de status à loja, no formato de mensagem da CJ
// (type ORDER, messageType UPDATE), com assinatura HMAC, retry e ordem por pedido.
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
	"sync"
	"time"

	"github.com/google/uuid"
)

// SignatureHeader leva "sha256=<hex>" do HMAC do corpo cru. Esquema próprio do simulador:
// o da CJ real não foi verificado.
const SignatureHeader = "X-Sim-Signature"

type Message struct {
	MessageID   string `json:"messageId"`
	Type        string `json:"type"`
	MessageType string `json:"messageType"`
	Params      Params `json:"params"`
}

type Params struct {
	OrderID          string `json:"orderId"`
	OrderNumber      string `json:"orderNumber"`
	OrderStatus      string `json:"orderStatus"`
	SubStatus        string `json:"subStatus"`
	TrackNumber      string `json:"trackNumber"`
	TrackingProvider string `json:"trackingProvider"`
	TrackingURL      string `json:"trackingUrl"`
	Reason           string `json:"reason"`
	UpdateDate       string `json:"updateDate"`
}

type Options struct {
	URL       string
	Secret    string
	Attempts  int
	RetryBase time.Duration
	Duplicate bool
	Timeout   time.Duration
}

// Dispatcher mantém uma fila por pedido: cada pedido tem no máximo uma entrega em andamento,
// então SHIPPED só sai depois que UNSHIPPED foi entregue (ou esgotou as tentativas). Pedidos
// diferentes não se bloqueiam.
type Dispatcher struct {
	opts   Options
	client *http.Client
	log    *slog.Logger

	ctx    context.Context
	cancel context.CancelFunc
	wg     sync.WaitGroup

	mu      sync.Mutex
	queues  map[string][]Message
	running map[string]bool
}

func NewDispatcher(opts Options, log *slog.Logger) *Dispatcher {
	if opts.Attempts <= 0 {
		opts.Attempts = 5
	}
	if opts.Timeout <= 0 {
		opts.Timeout = 5 * time.Second
	}
	ctx, cancel := context.WithCancel(context.Background())
	return &Dispatcher{
		opts:    opts,
		client:  &http.Client{Timeout: opts.Timeout},
		log:     log,
		ctx:     ctx,
		cancel:  cancel,
		queues:  map[string][]Message{},
		running: map[string]bool{},
	}
}

// Enqueue agenda a mensagem na fila do pedido e volta na hora. Sem URL configurada, descarta.
func (d *Dispatcher) Enqueue(p Params) {
	if d.opts.URL == "" {
		return
	}
	msg := Message{MessageID: uuid.NewString(), Type: "ORDER", MessageType: "UPDATE", Params: p}
	d.mu.Lock()
	defer d.mu.Unlock()
	if d.ctx.Err() != nil {
		return
	}
	d.queues[p.OrderID] = append(d.queues[p.OrderID], msg)
	if !d.running[p.OrderID] {
		d.running[p.OrderID] = true
		d.wg.Add(1)
		go d.drain(p.OrderID)
	}
}

// Wait bloqueia até as filas esvaziarem (usado nos testes e no desligamento).
func (d *Dispatcher) Wait() { d.wg.Wait() }

// Close interrompe retries pendentes e espera as entregas em andamento terminarem.
func (d *Dispatcher) Close() {
	d.mu.Lock()
	d.cancel()
	d.mu.Unlock()
	d.wg.Wait()
}

func (d *Dispatcher) drain(orderID string) {
	defer d.wg.Done()
	for {
		d.mu.Lock()
		q := d.queues[orderID]
		if len(q) == 0 {
			delete(d.queues, orderID)
			delete(d.running, orderID)
			d.mu.Unlock()
			return
		}
		msg := q[0]
		d.queues[orderID] = q[1:]
		d.mu.Unlock()

		d.deliver(msg)
		if d.opts.Duplicate {
			d.deliver(msg) // mesmo messageId: exercita a deduplicação de quem recebe
		}
	}
}

// deliver tenta até Attempts vezes, com espera RetryBase, 2x, 4x... entre elas.
func (d *Dispatcher) deliver(msg Message) {
	body, err := json.Marshal(msg)
	if err != nil {
		d.log.Error("webhook: falha ao serializar", "error", err.Error())
		return
	}
	sig := "sha256=" + Sign(body, d.opts.Secret)
	log := d.log.With("message_id", msg.MessageID, "order_id", msg.Params.OrderID, "status", msg.Params.OrderStatus)

	wait := d.opts.RetryBase
	for attempt := 1; attempt <= d.opts.Attempts; attempt++ {
		err := d.post(body, sig)
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
			log.Warn("webhook abandonado no desligamento")
			return
		case <-time.After(wait):
		}
		wait *= 2
	}
}

func (d *Dispatcher) post(body []byte, sig string) error {
	req, err := http.NewRequestWithContext(d.ctx, http.MethodPost, d.opts.URL, bytes.NewReader(body))
	if err != nil {
		return fmt.Errorf("requisição: %w", err)
	}
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set(SignatureHeader, sig)
	resp, err := d.client.Do(req)
	if err != nil {
		return fmt.Errorf("envio: %w", err)
	}
	defer resp.Body.Close()
	_, _ = io.Copy(io.Discard, resp.Body) // esvazia para reaproveitar a conexão
	if resp.StatusCode < 200 || resp.StatusCode > 299 {
		return fmt.Errorf("destino respondeu %d", resp.StatusCode)
	}
	return nil
}

// Sign devolve o HMAC-SHA256 do corpo em hex, sem o prefixo "sha256=".
func Sign(body []byte, secret string) string {
	m := hmac.New(sha256.New, []byte(secret))
	_, _ = m.Write(body)
	return hex.EncodeToString(m.Sum(nil))
}
