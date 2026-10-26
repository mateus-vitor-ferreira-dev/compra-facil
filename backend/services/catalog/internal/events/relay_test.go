package events

import (
	"context"
	"errors"
	"log/slog"
	"sync"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// fakeOutbox guarda eventos em memória e só os apaga quando publish dá certo, como o repositório.
type fakeOutbox struct {
	mu      sync.Mutex
	pending []Message
	drains  int
}

func (f *fakeOutbox) Drain(ctx context.Context, limit int, publish func(context.Context, []Message) error) (int, error) {
	f.mu.Lock()
	defer f.mu.Unlock()
	f.drains++
	batch := f.pending[:min(limit, len(f.pending))]
	if len(batch) == 0 {
		return 0, nil
	}
	if err := publish(ctx, batch); err != nil {
		return 0, err
	}
	f.pending = f.pending[len(batch):]
	return len(batch), nil
}

func (f *fakeOutbox) left() int {
	f.mu.Lock()
	defer f.mu.Unlock()
	return len(f.pending)
}

// flakyBroker falha nas primeiras `failures` chamadas e depois aceita tudo.
type flakyBroker struct {
	mu        sync.Mutex
	failures  int
	calls     int
	published []Message
}

func (b *flakyBroker) Publish(_ context.Context, msgs []Message) error {
	b.mu.Lock()
	defer b.mu.Unlock()
	b.calls++
	if b.calls <= b.failures {
		return errors.New("broker fora")
	}
	b.published = append(b.published, msgs...)
	return nil
}

func runRelay(t *testing.T, outbox Outbox, pub Publisher, opts RelayOptions) {
	t.Helper()
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan struct{})
	go func() {
		defer close(done)
		NewRelay(outbox, pub, opts, slog.New(slog.DiscardHandler)).Run(ctx)
	}()
	t.Cleanup(func() {
		cancel()
		select {
		case <-done:
		case <-time.After(time.Second):
			t.Error("o relay não parou ao cancelar o contexto")
		}
	})
}

func messages(n int) []Message {
	out := make([]Message, n)
	for i := range out {
		out[i] = Message{Key: string(rune('a' + i))}
	}
	return out
}

func TestRelay_RetriesUntilBrokerComesBack(t *testing.T) {
	outbox := &fakeOutbox{pending: messages(3)}
	broker := &flakyBroker{failures: 3}
	runRelay(t, outbox, broker, RelayOptions{PollInterval: time.Millisecond, BatchSize: 10, MaxBackoff: 5 * time.Millisecond})

	require.Eventually(t, func() bool { return outbox.left() == 0 }, time.Second, time.Millisecond)
	broker.mu.Lock()
	defer broker.mu.Unlock()
	assert.Equal(t, messages(3), broker.published, "tudo sai, na ordem, depois das falhas")
	assert.Equal(t, 4, broker.calls)
}

func TestRelay_FullBatchDrainsWithoutWaiting(t *testing.T) {
	outbox := &fakeOutbox{pending: messages(10)}
	broker := &flakyBroker{}
	// com PollInterval de 1h, só esvazia se lote cheio não esperar
	runRelay(t, outbox, broker, RelayOptions{PollInterval: time.Hour, BatchSize: 2, MaxBackoff: time.Hour})

	require.Eventually(t, func() bool { return outbox.left() == 0 }, time.Second, time.Millisecond)
}

func TestRelay_StopsOnCancel(t *testing.T) {
	outbox := &fakeOutbox{}
	runRelay(t, outbox, &flakyBroker{}, RelayOptions{PollInterval: time.Hour, BatchSize: 10, MaxBackoff: time.Hour})
	require.Eventually(t, func() bool {
		outbox.mu.Lock()
		defer outbox.mu.Unlock()
		return outbox.drains > 0
	}, time.Second, time.Millisecond)
	// o Cleanup de runRelay falha o teste se o Run não voltar logo após o cancel
}
