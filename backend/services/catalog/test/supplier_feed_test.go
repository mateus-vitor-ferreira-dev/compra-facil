package test

import (
	"context"
	"encoding/json"
	"log/slog"
	"os"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"github.com/twmb/franz-go/pkg/kadm"
	"github.com/twmb/franz-go/pkg/kgo"
	"gorm.io/gorm"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/messaging"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/repository"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/service"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/supplierfeed"
)

// Critério da #217: o feed do fornecedor entra na loja com o preço pela regra de margem, mudar o custo
// muda o preço, e o consumo segue o ADR 0003 (inbox, retry, DLT e reprocessamento).
func TestSupplierFeed_EndToEnd(t *testing.T) {
	if testing.Short() {
		t.Skip("integração (Postgres + Redpanda)")
	}
	ctx := context.Background()
	log := slog.New(slog.NewTextHandler(os.Stderr, &slog.HandlerOptions{Level: slog.LevelError}))
	db := startPostgres(t, log)
	brokers := startRedpanda(t)

	fones := domain.Category{Slug: "fones-over-ear", Name: "Fones over-ear"}
	require.NoError(t, repository.NewCategoryRepository(db).Create(ctx, &fones))
	batons := domain.Category{Slug: "batons", Name: "Batons"}
	require.NoError(t, repository.NewCategoryRepository(db).Create(ctx, &batons))

	invalidated := make(chan struct{}, 100)
	// sobe o consumer com o mapeamento de categorias dado; devolve como pará-lo
	start := func(categories map[string]string) (*messaging.Consumer, func()) {
		consumer, err := messaging.NewConsumer(db, messaging.Options{
			Brokers: brokers, Service: "catalog", InPlaceAttempts: 1, RetryDelays: []time.Duration{200 * time.Millisecond},
			PollInterval: 50 * time.Millisecond, MaxBackoff: 200 * time.Millisecond,
		}, log)
		require.NoError(t, err)
		supplierfeed.Register(consumer, service.SupplierOptions{
			Rates: domain.ExchangeRates{"USD": 5400}, Categories: categories,
			DefaultRule: domain.PricingRule{MarginPercent: 80, MinPriceCents: 1990}, PublishNew: true,
		}, log)
		consumer.AfterCommit(func(context.Context, messaging.Event) { invalidated <- struct{}{} })
		runCtx, cancel := context.WithCancel(ctx)
		done := make(chan struct{})
		go func() { defer close(done); _ = consumer.Run(runCtx) }()
		var once bool
		return consumer, func() {
			if !once {
				once = true
				cancel()
				<-done
				consumer.Close()
			}
		}
	}

	producer := newClient(t, brokers, kgo.AllowAutoTopicCreation())
	admin := kadm.NewClient(producer)
	_, err := admin.CreateTopics(ctx, 3, -1, nil, supplierfeed.Topic)
	require.NoError(t, err)

	consumer, stop := start(map[string]string{"Headphones": "fones-over-ear"}) // Lipstick fica de fora, de propósito
	t.Cleanup(stop)

	t0 := time.Date(2026, 10, 29, 12, 0, 0, 0, time.UTC)
	publish := func(eventID uuid.UUID, eventType, key string, data any) {
		t.Helper()
		value, err := json.Marshal(map[string]any{
			"event_id": eventID, "event_type": eventType, "event_version": 1, "occurred_at": t0,
			"producer": "fulfillment", "correlation_id": "teste", "causation_id": nil, "data": data,
		})
		require.NoError(t, err)
		require.NoError(t, producer.ProduceSync(ctx, &kgo.Record{Topic: supplierfeed.Topic, Key: []byte(key), Value: value}).FirstErr())
	}
	updated := func(vid, category string, cents int64, at time.Time) map[string]any {
		return map[string]any{"supplier": "cj", "vid": vid, "sku": "CJ" + vid, "name": "Wireless Headphones " + vid,
			"category": category, "cost": map[string]any{"amount_cents": cents, "currency": "USD"},
			"image_url": "https://img.example/" + vid + ".png", "weight_grams": 210, "updated_at": at}
	}
	products := repository.NewProductRepository(db)
	find := func(vid string) func() (domain.Product, bool) {
		return func() (domain.Product, bool) {
			p, err := products.FindBySupplier(ctx, "cj", vid, time.Now())
			return p, err == nil
		}
	}

	// 1. produto novo: entra à venda, com o preço pela margem (US$ 12,40 × 5,40 + 80% = R$ 120,90)
	first := uuid.New()
	publish(first, supplierfeed.ProductUpdated, "SIM-V-001", updated("SIM-V-001", "Headphones", 1240, t0))
	p := eventually(t, find("SIM-V-001"))
	assert.Equal(t, domain.ProductActive, p.Status)
	assert.Equal(t, int64(12090), p.PriceCents)
	assert.Equal(t, fones.ID, p.CategoryID)
	assert.Equal(t, "SIM-V-001", p.SKU)

	// 2. o mesmo evento de novo (redelivery) e um mais velho (retry atrasado): nada muda
	publish(first, supplierfeed.ProductUpdated, "SIM-V-001", updated("SIM-V-001", "Headphones", 9999, t0))
	publish(uuid.New(), supplierfeed.ProductUpdated, "SIM-V-001", updated("SIM-V-001", "Headphones", 9999, t0.Add(-time.Hour)))

	// 3. o fornecedor reajustou: o preço acompanha (US$ 15,90 → R$ 154,90) e sai o ProductPriceChanged.
	// A data vem como o .NET escreve, com o '+' do fuso escapado
	dotnet := updated("SIM-V-001", "Headphones", 1590, t0)
	dotnet["updated_at"] = json.RawMessage(`"2026-10-29T12:01:00.000000\u002B00:00"`)
	publish(uuid.New(), supplierfeed.ProductUpdated, "SIM-V-001", dotnet)
	p = eventually(t, func() (domain.Product, bool) {
		p, ok := find("SIM-V-001")()
		return p, ok && p.PriceCents != 12090
	})
	assert.Equal(t, int64(15490), p.PriceCents)
	assert.Equal(t, int64(1590), p.Supplier.CostCents)
	assert.Equal(t, 2, p.Version, "o repetido e o atrasado não mudaram nada")
	assert.Equal(t, int64(1), countOutbox(t, db, "ProductPriceChanged"))

	// 4. saiu do feed: fica inativo
	publish(uuid.New(), supplierfeed.ProductRemoved, "SIM-V-001", map[string]any{"supplier": "cj", "vid": "SIM-V-001", "removed_at": t0.Add(2 * time.Minute)})
	p = eventually(t, func() (domain.Product, bool) {
		p, ok := find("SIM-V-001")()
		return p, ok && p.Status == domain.ProductInactive
	})
	assert.NotNil(t, p.Supplier.RemovedAt)

	// 5. categoria sem mapeamento: falha, passa pelo retry e para na DLT; configurada, o reprocessamento a importa
	publish(uuid.New(), supplierfeed.ProductUpdated, "SIM-V-019", updated("SIM-V-019", "Lipstick", 310, t0))
	dlt := consumer.DeadLetters()
	letters := eventually(t, func() ([]messaging.DeadLetter, bool) {
		l, err := dlt.Pending(ctx, 10)
		return l, err == nil && len(l) == 1
	})
	assert.Equal(t, supplierfeed.Topic, letters[0].Topic)
	assert.Equal(t, 2, letters[0].Attempts, "a origem e o nível de retry")
	assert.Contains(t, *letters[0].Error, "categoria do fornecedor sem categoria da loja")

	// a equipe configura (CATALOG_SUPPLIER_CATEGORIES), o serviço sobe de novo, e reprocessa
	stop()
	consumer, stop = start(map[string]string{"Headphones": "fones-over-ear", "Lipstick": "batons"})
	t.Cleanup(stop)
	dlt = consumer.DeadLetters()
	replayed, err := dlt.Replay(ctx)
	require.NoError(t, err)
	assert.Equal(t, 1, replayed)
	lipstick := eventually(t, find("SIM-V-019"))
	assert.Equal(t, batons.ID, lipstick.CategoryID)
	assert.Equal(t, int64(3090), lipstick.PriceCents) // US$ 3,10 × 5,40 + 80% = R$ 30,14 → R$ 30,90
	left, err := dlt.Pending(ctx, 10)
	require.NoError(t, err)
	assert.Empty(t, left, "o reprocessado não volta a aparecer como pendente")

	// 6. mensagem envenenada: direto para a DLT, sem travar a partição
	require.NoError(t, producer.ProduceSync(ctx, &kgo.Record{Topic: supplierfeed.Topic, Key: []byte("x"), Value: []byte("não é json")}).FirstErr())
	poison := eventually(t, func() ([]messaging.DeadLetter, bool) {
		l, err := dlt.Pending(ctx, 10)
		return l, err == nil && len(l) == 1
	})
	assert.Nil(t, poison[0].EventID)
	assert.Equal(t, 1, poison[0].Attempts)

	assert.NotEmpty(t, invalidated, "o cache da vitrine é invalidado depois de cada evento gravado")
}

func eventually[T any](t *testing.T, try func() (T, bool)) T {
	t.Helper()
	deadline := time.Now().Add(30 * time.Second)
	for {
		if v, ok := try(); ok {
			return v
		}
		if time.Now().After(deadline) {
			t.Fatal("tempo esgotado")
		}
		time.Sleep(100 * time.Millisecond)
	}
}

func countOutbox(t *testing.T, db *gorm.DB, eventType string) int64 {
	t.Helper()
	var n int64
	require.NoError(t, db.Table("outbox").Where("event_type = ?", eventType).Count(&n).Error)
	return n
}
