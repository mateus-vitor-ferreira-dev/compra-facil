// Package test tem os testes de integração do catálogo com dependências reais (Postgres, Redpanda).
package test

import (
	"context"
	"encoding/json"
	"log/slog"
	"os"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"github.com/testcontainers/testcontainers-go"
	tcpostgres "github.com/testcontainers/testcontainers-go/modules/postgres"
	tcredpanda "github.com/testcontainers/testcontainers-go/modules/redpanda"
	"github.com/twmb/franz-go/pkg/kadm"
	"github.com/twmb/franz-go/pkg/kgo"
	"gorm.io/gorm"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/config"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/events"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/repository"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/migrations"
)

// Mesmas imagens do compose.
const (
	postgresImage = "postgres:17.11-alpine"
	redpandaImage = "redpandadata/redpanda:v26.2.5"
)

// Critério da #68: com o Kafka fora, a escrita continua dando certo e o evento fica no outbox;
// quando o Kafka volta, o evento sai, em ordem e sem perda.
func TestOutbox_EventSurvivesKafkaOutage(t *testing.T) {
	if testing.Short() {
		t.Skip("integração (Postgres + Redpanda)")
	}
	ctx := context.Background()
	log := slog.New(slog.NewTextHandler(os.Stderr, &slog.HandlerOptions{Level: slog.LevelWarn}))
	db := startPostgres(t, log)

	// 1. escrita com o Kafka fora do ar
	cat := domain.Category{Slug: "audio", Name: "Áudio"}
	require.NoError(t, repository.NewCategoryRepository(db).Create(ctx, &cat))
	products := repository.NewProductRepository(db)
	p := domain.Product{SKU: "AUD-1", Name: "Fone", CategoryID: cat.ID, PriceCents: 19990, Status: domain.ProductActive}
	require.NoError(t, products.Create(ctx, &p), "a escrita não depende do Kafka")
	p.PriceCents = 14990
	require.NoError(t, products.Update(ctx, &p, 1))

	outbox := repository.NewOutboxRepository(db)
	down, err := events.NewKafka(events.KafkaOptions{Brokers: []string{"127.0.0.1:1"}, Timeout: 2 * time.Second})
	require.NoError(t, err)
	t.Cleanup(down.Close)
	_, err = outbox.Drain(ctx, 100, down.Publish)
	require.Error(t, err, "sem broker a publicação falha")
	assert.Equal(t, int64(3), pending(t, db), "Created + Updated + PriceChanged continuam no outbox")

	// 2. o Kafka volta: o relay publica tudo
	brokers := startRedpanda(t)
	up, err := events.NewKafka(events.KafkaOptions{Brokers: brokers, Timeout: 10 * time.Second})
	require.NoError(t, err)
	t.Cleanup(up.Close)

	relayCtx, stop := context.WithCancel(ctx)
	done := make(chan struct{})
	go func() {
		defer close(done)
		events.NewRelay(outbox, up, events.RelayOptions{
			PollInterval: 50 * time.Millisecond, BatchSize: 100, MaxBackoff: time.Second,
		}, log).Run(relayCtx)
	}()
	t.Cleanup(func() { stop(); <-done })

	require.Eventually(t, func() bool { return pending(t, db) == 0 }, 30*time.Second, 100*time.Millisecond,
		"o outbox esvazia depois que o Kafka volta")

	// 3. o que chegou no tópico: os 3 eventos, na ordem, com a chave do produto
	got := consume(t, brokers, events.TopicProducts, 3)
	var typesSeen []string
	for _, r := range got {
		assert.Equal(t, p.ID.String(), string(r.Key))
		var env struct {
			EventType string `json:"event_type"`
		}
		require.NoError(t, json.Unmarshal(r.Value, &env))
		typesSeen = append(typesSeen, env.EventType)
	}
	assert.Equal(t, []string{events.TypeProductCreated, events.TypeProductUpdated, events.TypeProductPriceChanged}, typesSeen)

	// 4. tópicos criados com a configuração do contrato (não pelo auto-create do broker)
	admin := kadm.NewClient(newClient(t, brokers))
	details, err := admin.ListTopics(ctx, events.TopicProducts, events.TopicCampaigns)
	require.NoError(t, err)
	for _, topic := range []string{events.TopicProducts, events.TopicCampaigns} {
		require.Contains(t, details, topic)
		assert.Len(t, details[topic].Partitions, 3, topic)
	}
}

func startPostgres(t *testing.T, log *slog.Logger) *gorm.DB {
	t.Helper()
	ctx := context.Background()
	ctr, err := tcpostgres.Run(ctx, postgresImage,
		tcpostgres.WithDatabase("catalog"), tcpostgres.WithUsername("catalog"), tcpostgres.WithPassword("catalog"),
		tcpostgres.BasicWaitStrategies(),
	)
	testcontainers.CleanupContainer(t, ctr)
	require.NoError(t, err)
	dsn, err := ctr.ConnectionString(ctx, "sslmode=disable")
	require.NoError(t, err)
	require.NoError(t, migrations.Up(dsn))
	db, err := repository.Open(config.DBConfig{
		DSN: dsn, MaxOpenConns: 5, MaxIdleConns: 2, ConnMaxLifetime: time.Minute, SlowQuery: time.Second,
	}, log)
	require.NoError(t, err)
	return db
}

func startRedpanda(t *testing.T) []string {
	t.Helper()
	ctx := context.Background()
	ctr, err := tcredpanda.Run(ctx, redpandaImage)
	testcontainers.CleanupContainer(t, ctr)
	require.NoError(t, err)
	broker, err := ctr.KafkaSeedBroker(ctx)
	require.NoError(t, err)
	return []string{broker}
}

func pending(t *testing.T, db *gorm.DB) int64 {
	t.Helper()
	var n int64
	require.NoError(t, db.Table("outbox").Count(&n).Error)
	return n
}

func newClient(t *testing.T, brokers []string, opts ...kgo.Opt) *kgo.Client {
	t.Helper()
	c, err := kgo.NewClient(append([]kgo.Opt{kgo.SeedBrokers(brokers...)}, opts...)...)
	require.NoError(t, err)
	t.Cleanup(c.Close)
	return c
}

// consume lê n registros do tópico desde o início, ordenados por offset dentro da partição.
func consume(t *testing.T, brokers []string, topic string, n int) []*kgo.Record {
	t.Helper()
	c := newClient(t, brokers, kgo.ConsumeTopics(topic), kgo.ConsumeResetOffset(kgo.NewOffset().AtStart()))
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	var out []*kgo.Record
	for len(out) < n {
		fetches := c.PollFetches(ctx)
		require.NoError(t, ctx.Err(), "esperava %d registros, chegaram %d", n, len(out))
		fetches.EachRecord(func(r *kgo.Record) { out = append(out, r) })
	}
	return out
}
