package events

import (
	"context"
	"log/slog"
	"time"
)

// Outbox é a fila de eventos gravados junto com as escritas (repository.OutboxRepository).
type Outbox interface {
	// Drain entrega a publish os eventos mais antigos (até limit), em ordem, e os apaga se publish
	// der certo. Devolve quantos foram publicados.
	Drain(ctx context.Context, limit int, publish func(context.Context, []Message) error) (int, error)
}

// Publisher entrega um lote ao broker; só devolve nil quando todos foram confirmados.
type Publisher interface {
	Publish(ctx context.Context, msgs []Message) error
}

type RelayOptions struct {
	// PollInterval é a espera quando o outbox está vazio.
	PollInterval time.Duration
	// BatchSize é o máximo de eventos por lote; lote cheio faz o próximo sair sem espera.
	BatchSize int
	// MaxBackoff limita a espera entre tentativas quando a publicação falha (Kafka fora).
	MaxBackoff time.Duration
}

// Relay move os eventos do outbox para o Kafka até o contexto acabar.
type Relay struct {
	outbox Outbox
	pub    Publisher
	opts   RelayOptions
	log    *slog.Logger
}

func NewRelay(outbox Outbox, pub Publisher, opts RelayOptions, log *slog.Logger) *Relay {
	return &Relay{outbox: outbox, pub: pub, opts: opts, log: log}
}

// Run roda o laço de publicação. Com o broker fora, os eventos ficam no outbox e a espera entre
// tentativas dobra até MaxBackoff; quando ele volta, tudo que acumulou sai em ordem.
func (r *Relay) Run(ctx context.Context) {
	backoff := r.opts.PollInterval
	failing := false
	for {
		n, err := r.outbox.Drain(ctx, r.opts.BatchSize, r.pub.Publish)
		wait := r.opts.PollInterval
		switch {
		case ctx.Err() != nil:
			return
		case err != nil:
			if !failing {
				r.log.WarnContext(ctx, "falha ao publicar eventos; ficam no outbox para nova tentativa",
					slog.String("error", err.Error()))
			}
			failing = true
			backoff = min(backoff*2, r.opts.MaxBackoff)
			wait = backoff
		default:
			if failing {
				r.log.InfoContext(ctx, "publicação de eventos normalizada")
			}
			failing = false
			backoff = r.opts.PollInterval
			if n == r.opts.BatchSize {
				wait = 0 // ainda há fila
			}
		}
		if !sleep(ctx, wait) {
			return
		}
	}
}

func sleep(ctx context.Context, d time.Duration) bool {
	if d <= 0 {
		return ctx.Err() == nil
	}
	t := time.NewTimer(d)
	defer t.Stop()
	select {
	case <-ctx.Done():
		return false
	case <-t.C:
		return true
	}
}
