package messaging

import (
	"context"
	"errors"
	"fmt"
	"strconv"
	"sync"
	"time"

	"github.com/twmb/franz-go/pkg/kadm"
	"github.com/twmb/franz-go/pkg/kerr"
	"github.com/twmb/franz-go/pkg/kgo"
)

// Configuração dos tópicos de retry e da DLT (do serviço, não são contrato).
const (
	failureTopicPartitions = 3
	retryRetention         = 7 * 24 * time.Hour
	// a DLT guarda mais: dá tempo de investigar e reprocessar
	deadLetterRetention = 30 * 24 * time.Hour
)

// failureProducer publica nos tópicos de retry e na DLT (o consumer encaminha, o reprocessamento
// devolve). Separado do relay: não passa pelo outbox, a mensagem já existe no Kafka.
type failureProducer struct {
	opts   Options
	client *kgo.Client
	admin  *kadm.Client

	mu    sync.Mutex
	ready bool
}

func newFailureProducer(opts Options) (*failureProducer, error) {
	client, err := kgo.NewClient(
		kgo.SeedBrokers(opts.Brokers...),
		kgo.ClientID(opts.Service+"-failures"),
		kgo.RequiredAcks(kgo.AllISRAcks()),
	)
	if err != nil {
		return nil, fmt.Errorf("producer de falhas: %w", err)
	}
	return &failureProducer{opts: opts, client: client, admin: kadm.NewClient(client)}, nil
}

// ensureTopics cria os tópicos de retry e a DLT (uma vez por processo); existente não é alterado.
func (p *failureProducer) ensureTopics(ctx context.Context) error {
	p.mu.Lock()
	defer p.mu.Unlock()
	if p.ready {
		return nil
	}
	cleanup := "delete"
	create := func(retention time.Duration, topics ...string) error {
		ms := strconv.FormatInt(retention.Milliseconds(), 10)
		resp, err := p.admin.CreateTopics(ctx, failureTopicPartitions, -1,
			map[string]*string{"retention.ms": &ms, "cleanup.policy": &cleanup}, topics...)
		if err != nil {
			return fmt.Errorf("criando tópicos %v: %w", topics, err)
		}
		for _, t := range resp.Sorted() {
			if t.Err != nil && !errors.Is(t.Err, kerr.TopicAlreadyExists) {
				return fmt.Errorf("criando tópico %s: %w", t.Topic, t.Err)
			}
		}
		return nil
	}
	retries := make([]string, 0, len(p.opts.RetryDelays))
	for tier := 1; tier <= len(p.opts.RetryDelays); tier++ {
		retries = append(retries, p.opts.retryTopic(tier))
	}
	if len(retries) > 0 {
		if err := create(retryRetention, retries...); err != nil {
			return err
		}
	}
	if err := create(deadLetterRetention, p.opts.deadLetterTopic()); err != nil {
		return err
	}
	p.ready = true
	return nil
}

func (p *failureProducer) produce(ctx context.Context, records ...*kgo.Record) error {
	if err := p.ensureTopics(ctx); err != nil {
		return err
	}
	if err := p.client.ProduceSync(ctx, records...).FirstErr(); err != nil {
		return fmt.Errorf("publicando %d mensagens: %w", len(records), err)
	}
	return nil
}

func (p *failureProducer) close() { p.client.Close() }
