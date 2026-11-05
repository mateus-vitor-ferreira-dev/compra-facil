package efi

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"net/url"
	"sync"
	"time"
)

type DispatcherOptions struct {
	Attempts  int
	RetryBase time.Duration
	Timeout   time.Duration
}

// Dispatcher entrega os avisos da Efí: o POST com o token da cobrança de cartão (form notification=<token>,
// sem assinatura: quem recebe consulta GET /v1/notification/:token) e o webhook do PIX ({"pix": [...]}).
// Na ordem de cada cobrança, uma de cada vez, com retry; cobranças diferentes não se bloqueiam.
type Dispatcher struct {
	opts   DispatcherOptions
	client *http.Client
	log    *slog.Logger

	ctx    context.Context
	cancel context.CancelFunc
	wg     sync.WaitGroup

	mu      sync.Mutex
	queues  map[string][]delivery
	running map[string]bool
}

type delivery struct {
	url, contentType string
	body             []byte
}

func NewDispatcher(opts DispatcherOptions, log *slog.Logger) *Dispatcher {
	if opts.Attempts <= 0 {
		opts.Attempts = 5
	}
	if opts.RetryBase <= 0 {
		opts.RetryBase = time.Second
	}
	if opts.Timeout <= 0 {
		opts.Timeout = 5 * time.Second
	}
	ctx, cancel := context.WithCancel(context.Background())
	return &Dispatcher{opts: opts, client: &http.Client{Timeout: opts.Timeout}, log: log, ctx: ctx, cancel: cancel,
		queues: map[string][]delivery{}, running: map[string]bool{}}
}

func (d *Dispatcher) ChargeChanged(target, token string) {
	form := url.Values{"notification": {token}}
	d.enqueue("charge:"+token, delivery{url: target, contentType: "application/x-www-form-urlencoded", body: []byte(form.Encode())})
}

func (d *Dispatcher) PixChanged(target string, item PixItem) {
	body, err := json.Marshal(map[string][]PixItem{"pix": {item}})
	if err != nil {
		d.log.Error("webhook do PIX: falha ao serializar", "error", err.Error())
		return
	}
	d.enqueue("pix:"+item.Txid, delivery{url: target, contentType: "application/json", body: body})
}

func (d *Dispatcher) enqueue(key string, x delivery) {
	d.mu.Lock()
	defer d.mu.Unlock()
	if d.ctx.Err() != nil {
		return
	}
	d.queues[key] = append(d.queues[key], x)
	if !d.running[key] {
		d.running[key] = true
		d.wg.Add(1)
		go d.drain(key)
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

func (d *Dispatcher) drain(key string) {
	defer d.wg.Done()
	for {
		d.mu.Lock()
		q := d.queues[key]
		if len(q) == 0 {
			delete(d.queues, key)
			delete(d.running, key)
			d.mu.Unlock()
			return
		}
		next := q[0]
		d.queues[key] = q[1:]
		d.mu.Unlock()
		d.deliver(key, next)
	}
}

func (d *Dispatcher) deliver(key string, x delivery) {
	log := d.log.With("key", key)
	wait := d.opts.RetryBase
	for attempt := 1; attempt <= d.opts.Attempts; attempt++ {
		err := d.post(x)
		if err == nil {
			log.Info("aviso entregue", "attempt", attempt)
			return
		}
		if attempt == d.opts.Attempts {
			log.Error("aviso descartado após esgotar as tentativas", "attempts", attempt, "error", err.Error())
			return
		}
		log.Warn("aviso falhou, nova tentativa agendada", "attempt", attempt, "retry_in", wait.String(), "error", err.Error())
		select {
		case <-d.ctx.Done():
			return
		case <-time.After(wait):
		}
		wait *= 2
	}
}

func (d *Dispatcher) post(x delivery) error {
	req, err := http.NewRequestWithContext(d.ctx, http.MethodPost, x.url, bytes.NewReader(x.body))
	if err != nil {
		return fmt.Errorf("requisição: %w", err)
	}
	req.Header.Set("Content-Type", x.contentType)
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
