// Package messaging consome eventos de outros serviços com a mesma semântica dos serviços .NET
// (ADR 0003): inbox na transação do efeito (evento repetido não repete nada), tentativas no lugar,
// tópicos de retry com atraso crescente e uma dead letter topic, com leitura e reprocessamento.
package messaging

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"strings"
	"time"

	"github.com/google/uuid"
	"github.com/twmb/franz-go/pkg/kgo"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"
	"go.opentelemetry.io/otel/propagation"
	"go.opentelemetry.io/otel/trace"
	"gorm.io/gorm"
)

// ErrPoison marca a mensagem que nunca vai ser processada (fora do contrato): vai direto para a DLT.
var ErrPoison = errors.New("mensagem fora do contrato")

// Envelope é o EventEnvelope do contrato (contracts/asyncapi/common/v1/common.yaml), com o data cru.
type Envelope struct {
	EventID       uuid.UUID       `json:"event_id"`
	EventType     string          `json:"event_type"`
	EventVersion  int             `json:"event_version"`
	OccurredAt    Time            `json:"occurred_at"`
	Producer      string          `json:"producer"`
	CorrelationID string          `json:"correlation_id"`
	Data          json.RawMessage `json:"data"`
}

// Event é o que o handler recebe: o envelope e o tópico de origem.
type Event struct {
	Envelope
	Topic string
}

// Decode lê o data do evento; data fora do formato é mensagem envenenada.
func (e Event) Decode(v any) error {
	if err := json.Unmarshal(e.Data, v); err != nil {
		return fmt.Errorf("%w: data de %s: %w", ErrPoison, e.EventType, err)
	}
	return nil
}

// Handler trata um evento dentro da transação tx, a mesma que o marca como processado (inbox) e que
// grava os eventos que ele publicar (outbox): ou tudo entra, ou nada.
type Handler func(ctx context.Context, tx *gorm.DB, e Event) error

type Options struct {
	Brokers []string
	// Service nomeia o consumer group e os tópicos dele: <service>.retry-N e <service>.dlt.
	Service string
	// InPlaceAttempts antes de passar a mensagem para o nível seguinte (padrão 3).
	InPlaceAttempts int
	// RetryDelays é o atraso de cada tópico de retry, em ordem (padrão 5 s, 30 s, 2 min).
	RetryDelays  []time.Duration
	PollInterval time.Duration
	MaxBackoff   time.Duration
}

func (o Options) withDefaults() Options {
	if o.InPlaceAttempts <= 0 {
		o.InPlaceAttempts = 3
	}
	if o.RetryDelays == nil {
		o.RetryDelays = []time.Duration{5 * time.Second, 30 * time.Second, 2 * time.Minute}
	}
	if o.PollInterval <= 0 {
		o.PollInterval = time.Second
	}
	if o.MaxBackoff <= 0 {
		o.MaxBackoff = 30 * time.Second
	}
	return o
}

func (o Options) retryTopic(tier int) string { return fmt.Sprintf("%s.retry-%d", o.Service, tier) }

func (o Options) deadLetterTopic() string { return o.Service + ".dlt" }

type route struct{ topic, eventType string }

// Consumer assina os tópicos dos handlers registrados (o tópico de origem e cada nível de retry, cada
// um num laço e num consumer group próprios). event_type sem handler é pulado: é assim que o produtor
// evolui sem coordenar deploy.
type Consumer struct {
	opts     Options
	db       *gorm.DB
	failures *failureProducer
	handlers map[route]Handler
	log      *slog.Logger
	consumed metric.Int64Counter
	tracer   trace.Tracer
	// afterCommit roda depois do commit de cada evento processado (não nos repetidos)
	afterCommit func(ctx context.Context, e Event)
}

func NewConsumer(db *gorm.DB, opts Options, log *slog.Logger) (*Consumer, error) {
	opts = opts.withDefaults()
	if opts.Service == "" || len(opts.Brokers) == 0 {
		return nil, errors.New("messaging: Service e Brokers são obrigatórios")
	}
	failures, err := newFailureProducer(opts)
	if err != nil {
		return nil, err
	}
	consumed, err := otel.Meter("catalog/messaging").Int64Counter("compra_facil.messaging.consumed",
		metric.WithUnit("{event}"), metric.WithDescription("Eventos consumidos por tópico, tipo e resultado."))
	if err != nil {
		return nil, fmt.Errorf("métrica de consumo: %w", err)
	}
	return &Consumer{opts: opts, db: db, failures: failures, handlers: map[route]Handler{}, log: log,
		consumed: consumed, tracer: otel.Tracer("catalog/messaging")}, nil
}

// Handle registra o handler de um event_type de um tópico.
func (c *Consumer) Handle(topic, eventType string, h Handler) *Consumer {
	c.handlers[route{topic, eventType}] = h
	return c
}

// AfterCommit registra o que fazer depois que um evento é gravado (ex.: invalidar um cache): fora da
// transação, para não invalidar o que ainda pode ser desfeito.
func (c *Consumer) AfterCommit(fn func(ctx context.Context, e Event)) *Consumer {
	c.afterCommit = fn
	return c
}

// DeadLetters é a DLT deste consumer (para o painel listar e reprocessar).
func (c *Consumer) DeadLetters() *DeadLetterStore {
	return &DeadLetterStore{opts: c.opts, failures: c.failures, log: c.log}
}

func (c *Consumer) Close() { c.failures.close() }

type stage struct {
	tier   int
	topics []string
	group  string
}

// Run consome até o ctx acabar.
func (c *Consumer) Run(ctx context.Context) error {
	topics := map[string]bool{}
	for r := range c.handlers {
		topics[r.topic] = true
	}
	if len(topics) == 0 {
		return nil
	}
	source := make([]string, 0, len(topics))
	for t := range topics {
		source = append(source, t)
	}
	stages := []stage{{tier: 0, topics: source, group: c.opts.Service}}
	for tier := 1; tier <= len(c.opts.RetryDelays); tier++ {
		stages = append(stages, stage{tier: tier, topics: []string{c.opts.retryTopic(tier)}, group: c.opts.retryTopic(tier)})
	}
	errs := make(chan error, len(stages))
	for _, s := range stages {
		go func() { errs <- c.consumeLoop(ctx, s) }()
	}
	var first error
	for range stages {
		if err := <-errs; err != nil && first == nil {
			first = err
		}
	}
	return first
}

func (c *Consumer) consumeLoop(ctx context.Context, s stage) error {
	if s.tier > 0 {
		// os tópicos de retry são deste serviço: cria antes de assinar
		if err := c.failures.ensureTopics(ctx); err != nil {
			return err
		}
	}
	client, err := kgo.NewClient(
		kgo.SeedBrokers(c.opts.Brokers...),
		kgo.ClientID(s.group),
		kgo.ConsumerGroup(s.group),
		kgo.ConsumeTopics(s.topics...),
		kgo.DisableAutoCommit(), // offset só depois de processar (ou encaminhar)
		kgo.ConsumeResetOffset(kgo.NewOffset().AtStart()),
		kgo.BlockRebalanceOnPoll(),
		kgo.MetadataMaxAge(10*time.Second), // tópico criado depois da subida entra logo
	)
	if err != nil {
		return fmt.Errorf("consumer %s: %w", s.group, err)
	}
	defer func() {
		// com BlockRebalanceOnPoll, sair sem liberar o rebalance prende o LeaveGroup do Close
		client.AllowRebalance()
		client.Close() // sai do group na hora
	}()

	for {
		fetches := client.PollFetches(ctx)
		if ctx.Err() != nil {
			return nil //nolint:nilerr // desligando: a mensagem em andamento volta na subida
		}
		fetches.EachError(func(topic string, _ int32, err error) {
			c.log.WarnContext(ctx, "erro ao consumir", "topic", topic, "error", err.Error())
		})
		var stop bool
		fetches.EachRecord(func(r *kgo.Record) {
			if stop {
				return
			}
			if err := c.waitRetry(ctx, s, r); err != nil {
				stop = true
				return
			}
			if err := c.handle(ctx, s, r); err != nil {
				stop = true // só o cancelamento sai daqui sem a mensagem resolvida
				return
			}
			if err := client.CommitRecords(ctx, r); err != nil && ctx.Err() == nil {
				c.log.WarnContext(ctx, "falha ao confirmar offset", "topic", r.Topic, "error", err.Error())
			}
		})
		client.AllowRebalance()
		if stop {
			return nil
		}
	}
}

// waitRetry segura a mensagem de um nível de retry até a hora dela (limitada ao atraso do nível:
// header estranho não deixa o laço parado além dele).
func (c *Consumer) waitRetry(ctx context.Context, s stage, r *kgo.Record) error {
	if s.tier == 0 {
		return nil
	}
	wait := time.Until(retryAt(r))
	if limit := c.opts.RetryDelays[s.tier-1]; wait > limit {
		wait = limit
	}
	if wait <= 0 {
		return nil
	}
	select {
	case <-ctx.Done():
		return ctx.Err() //nolint:wrapcheck // cancelamento
	case <-time.After(wait):
		return nil
	}
}

// handle processa a mensagem ou a encaminha (retry seguinte ou DLT). Só sai com ela resolvida.
func (c *Consumer) handle(ctx context.Context, s stage, r *kgo.Record) error {
	// nos tópicos de retry, o handler e o inbox são os do tópico de origem
	topic := r.Topic
	if s.tier > 0 {
		if original, ok := header(r, headerOriginalTopic); ok {
			topic = original
		}
	}
	backoff := c.opts.PollInterval
	for attempt := 1; ; attempt++ {
		err := c.Process(ctx, topic, r)
		switch {
		case err == nil:
			return nil
		case ctx.Err() != nil:
			return ctx.Err() //nolint:wrapcheck // cancelamento
		case errors.Is(err, ErrPoison):
			c.log.ErrorContext(ctx, "mensagem fora do contrato", "topic", r.Topic, "partition", r.Partition, "offset", r.Offset, "error", err.Error())
			return c.forward(ctx, s, r, topic, c.opts.deadLetterTopic(), err)
		}
		c.count(ctx, topic, eventTypeOf(r), "error")
		c.log.WarnContext(ctx, "handler falhou", "topic", r.Topic, "partition", r.Partition, "offset", r.Offset,
			"attempt", attempt, "error", err.Error())
		if attempt >= c.opts.InPlaceAttempts {
			next := c.opts.deadLetterTopic()
			if s.tier < len(c.opts.RetryDelays) {
				next = c.opts.retryTopic(s.tier + 1)
			}
			return c.forward(ctx, s, r, topic, next, err)
		}
		if err := sleep(ctx, backoff); err != nil {
			return err
		}
		backoff = min(backoff*2, c.opts.MaxBackoff)
	}
}

// forward publica a mesma mensagem (chave, valor, trace) no retry ou na DLT. Com o Kafka fora, tenta
// até conseguir: o offset de origem só é confirmado depois.
func (c *Consumer) forward(ctx context.Context, s stage, r *kgo.Record, topic, target string, cause error) error {
	deadLetter := target == c.opts.deadLetterTopic()
	now := time.Now()
	var at *time.Time
	if !deadLetter {
		t := now.Add(c.opts.RetryDelays[s.tier])
		at = &t
	}
	copied := &kgo.Record{Topic: target, Key: r.Key, Value: r.Value, Headers: failureHeaders(r, topic, cause, now, at)}
	backoff := c.opts.PollInterval
	for {
		err := c.failures.produce(ctx, copied)
		if err == nil {
			break
		}
		if ctx.Err() != nil {
			return ctx.Err() //nolint:wrapcheck // cancelamento
		}
		c.log.WarnContext(ctx, "falha ao encaminhar; nova tentativa", "target", target, "error", err.Error())
		if err := sleep(ctx, backoff); err != nil {
			return err
		}
		backoff = min(backoff*2, c.opts.MaxBackoff)
	}
	if deadLetter {
		c.count(ctx, topic, eventTypeOf(r), "dead_lettered")
		c.log.ErrorContext(ctx, "mensagem foi para a DLT", "topic", topic, "partition", r.Partition, "offset", r.Offset, "dlt", target)
	} else {
		c.count(ctx, topic, eventTypeOf(r), "retried")
	}
	return nil
}

// Process trata uma mensagem do tópico de origem topic: o handler do event_type, na transação do inbox.
// Exportado para os testes chamarem sem o laço.
func (c *Consumer) Process(ctx context.Context, topic string, r *kgo.Record) error {
	var env Envelope
	if err := json.Unmarshal(r.Value, &env); err != nil || env.EventID == uuid.Nil || env.EventType == "" {
		return fmt.Errorf("%w: sem envelope válido", ErrPoison)
	}
	h, ok := c.handlers[route{topic, env.EventType}]
	if !ok {
		c.count(ctx, topic, env.EventType, "skipped")
		return nil
	}
	ctx = otel.GetTextMapPropagator().Extract(ctx, recordCarrier(r.Headers))
	ctx, span := c.tracer.Start(ctx, env.EventType+" process", trace.WithSpanKind(trace.SpanKindConsumer),
		trace.WithAttributes(attribute.String("messaging.system", "kafka"), attribute.String("messaging.destination.name", topic),
			attribute.String("messaging.message.id", env.EventID.String())))
	defer span.End()

	duplicate := false
	event := Event{Envelope: env, Topic: topic}
	err := c.db.WithContext(ctx).Transaction(func(tx *gorm.DB) error {
		res := tx.Exec(`INSERT INTO inbox_messages (event_id, topic, event_type, processed_at) VALUES (?, ?, ?, now())
			ON CONFLICT (event_id) DO NOTHING`, env.EventID, topic, env.EventType)
		if res.Error != nil {
			return res.Error
		}
		if res.RowsAffected == 0 {
			duplicate = true // já processado (redelivery, retry ou reprocessamento)
			return nil
		}
		return h(ctx, tx, event)
	})
	if err != nil {
		span.RecordError(err)
		return fmt.Errorf("%s %s: %w", env.EventType, env.EventID, err)
	}
	if duplicate {
		c.count(ctx, topic, env.EventType, "duplicate")
		return nil
	}
	c.count(ctx, topic, env.EventType, "processed")
	if c.afterCommit != nil {
		c.afterCommit(ctx, event)
	}
	return nil
}

func (c *Consumer) count(ctx context.Context, topic, eventType, result string) {
	c.consumed.Add(ctx, 1, metric.WithAttributes(attribute.String("topic", topic),
		attribute.String("event_type", eventType), attribute.String("result", result)))
}

func eventTypeOf(r *kgo.Record) string {
	var env struct {
		EventType string `json:"event_type"`
	}
	if json.Unmarshal(r.Value, &env) != nil || env.EventType == "" {
		return "-"
	}
	return env.EventType
}

func sleep(ctx context.Context, d time.Duration) error {
	select {
	case <-ctx.Done():
		return ctx.Err() //nolint:wrapcheck // cancelamento
	case <-time.After(d):
		return nil
	}
}

// recordCarrier lê o trace W3C dos headers da mensagem.
type recordCarrier []kgo.RecordHeader

var _ propagation.TextMapCarrier = recordCarrier(nil)

func (h recordCarrier) Get(key string) string {
	for i := len(h) - 1; i >= 0; i-- {
		if strings.EqualFold(h[i].Key, key) {
			return string(h[i].Value)
		}
	}
	return ""
}

func (recordCarrier) Set(string, string) {}

func (h recordCarrier) Keys() []string {
	keys := make([]string, len(h))
	for i, x := range h {
		keys[i] = x.Key
	}
	return keys
}
