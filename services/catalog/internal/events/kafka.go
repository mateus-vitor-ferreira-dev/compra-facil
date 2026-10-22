package events

import (
	"context"
	"errors"
	"fmt"
	"strconv"
	"sync/atomic"
	"time"

	"github.com/twmb/franz-go/pkg/kadm"
	"github.com/twmb/franz-go/pkg/kerr"
	"github.com/twmb/franz-go/pkg/kgo"
)

// Configuração dos tópicos do catálogo, igual aos bindings do contrato AsyncAPI.
const (
	topicPartitions = 3
	topicRetention  = 7 * 24 * time.Hour
)

type KafkaOptions struct {
	Brokers []string
	// Timeout é o máximo para o broker confirmar um lote; depois disso o lote volta para o outbox.
	Timeout time.Duration
}

// Kafka publica os eventos com o producer idempotente do franz-go (acks=all, sem duplicar em
// retries internos e mantendo a ordem por partição).
type Kafka struct {
	client      *kgo.Client
	admin       *kadm.Client
	topics      []string
	topicsReady atomic.Bool
}

func NewKafka(opts KafkaOptions) (*Kafka, error) {
	client, err := kgo.NewClient(
		kgo.SeedBrokers(opts.Brokers...),
		kgo.ClientID("catalog"),
		kgo.RecordDeliveryTimeout(opts.Timeout),
		kgo.ProduceRequestTimeout(opts.Timeout),
		kgo.ProducerLinger(5*time.Millisecond),
	)
	if err != nil {
		return nil, fmt.Errorf("cliente Kafka: %w", err)
	}
	return &Kafka{client: client, admin: kadm.NewClient(client), topics: []string{TopicProducts, TopicCampaigns}}, nil
}

// Publish envia o lote e espera a confirmação de todos. O franz-go mantém a ordem dos registros
// de uma mesma chave (mesma partição).
func (k *Kafka) Publish(ctx context.Context, msgs []Message) error {
	if err := k.ensureTopics(ctx); err != nil {
		return err
	}
	records := make([]*kgo.Record, len(msgs))
	for i, m := range msgs {
		headers := make([]kgo.RecordHeader, 0, len(m.Headers))
		for key, v := range m.Headers {
			headers = append(headers, kgo.RecordHeader{Key: key, Value: []byte(v)})
		}
		records[i] = &kgo.Record{Topic: m.Topic, Key: []byte(m.Key), Value: m.Payload, Headers: headers}
	}
	if err := k.client.ProduceSync(ctx, records...).FirstErr(); err != nil {
		return fmt.Errorf("publicando %d eventos: %w", len(msgs), err)
	}
	return nil
}

// ensureTopics cria os tópicos do catálogo com a configuração do contrato (uma vez por processo).
// Sem isto, o auto-create do broker criaria com 1 partição. Tópico existente não é alterado.
func (k *Kafka) ensureTopics(ctx context.Context) error {
	if k.topicsReady.Load() {
		return nil
	}
	retention := strconv.FormatInt(topicRetention.Milliseconds(), 10)
	cleanup := "delete"
	resp, err := k.admin.CreateTopics(ctx, topicPartitions, -1, map[string]*string{
		"retention.ms":   &retention,
		"cleanup.policy": &cleanup,
	}, k.topics...)
	if err != nil {
		return fmt.Errorf("criando tópicos: %w", err)
	}
	for _, t := range resp.Sorted() {
		if t.Err != nil && !errors.Is(t.Err, kerr.TopicAlreadyExists) {
			return fmt.Errorf("criando tópico %s: %w", t.Topic, t.Err)
		}
	}
	k.topicsReady.Store(true)
	return nil
}

func (k *Kafka) Close() { k.client.Close() }
