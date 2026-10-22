package repository

import (
	"context"
	"encoding/json"
	"errors"
	"testing"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"gorm.io/gorm"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/events"
)

// outboxEvent é a linha do outbox com o envelope decodificado, para as asserções.
type outboxEvent struct {
	Topic, Key, Type string
	CorrelationID    string
	Data             map[string]any
}

func readOutbox(t *testing.T) []outboxEvent {
	t.Helper()
	var recs []outboxRecord
	require.NoError(t, testDB.Order("id").Find(&recs).Error)
	out := make([]outboxEvent, len(recs))
	for i, rec := range recs {
		var env struct {
			EventID       uuid.UUID      `json:"event_id"`
			EventType     string         `json:"event_type"`
			CorrelationID string         `json:"correlation_id"`
			Data          map[string]any `json:"data"`
		}
		require.NoError(t, json.Unmarshal(rec.Payload, &env))
		require.Equal(t, rec.EventID, env.EventID, "coluna e envelope com o mesmo event_id")
		require.Equal(t, rec.EventType, env.EventType)
		out[i] = outboxEvent{Topic: rec.Topic, Key: rec.MessageKey, Type: rec.EventType, CorrelationID: env.CorrelationID, Data: env.Data}
	}
	return out
}

func types(evs []outboxEvent) []string {
	out := make([]string, len(evs))
	for i, e := range evs {
		out[i] = e.Type
	}
	return out
}

func TestProductWrites_EnqueueEventsInTheSameTransaction(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	cat := mustCategory(t, "audio", nil)
	reqCtx := events.WithCorrelationID(ctx, "req-abc")

	p := domain.Product{SKU: "AUD-1", Name: "Fone", CategoryID: cat.ID, PriceCents: 10000, Status: domain.ProductActive}
	require.NoError(t, repo.Create(reqCtx, &p))

	p.Name = "Fone Pro" // sem mudar preço
	require.NoError(t, repo.Update(reqCtx, &p, 1))
	p.PriceCents = 8990
	require.NoError(t, repo.Update(reqCtx, &p, 2))
	require.NoError(t, repo.Delete(reqCtx, p.ID))

	evs := readOutbox(t)
	assert.Equal(t, []string{
		events.TypeProductCreated,
		events.TypeProductUpdated,
		events.TypeProductUpdated, events.TypeProductPriceChanged,
		events.TypeProductDeleted,
	}, types(evs))
	for _, e := range evs {
		assert.Equal(t, events.TopicProducts, e.Topic)
		assert.Equal(t, p.ID.String(), e.Key)
		assert.Equal(t, "req-abc", e.CorrelationID)
	}
	versions := []float64{1, 2, 3, 3, 4}
	for i, e := range evs {
		assert.InDelta(t, versions[i], e.Data["version"], 0, "evento %d (%s)", i, e.Type)
	}
	assert.InDelta(t, 10000, evs[3].Data["old_price"].(map[string]any)["amount_cents"], 0)
	assert.InDelta(t, 8990, evs[3].Data["new_price"].(map[string]any)["amount_cents"], 0)
	assert.Equal(t, "AUD-1", evs[4].Data["sku"])
}

func TestProductWrites_FailureEnqueuesNothing(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	cat := mustCategory(t, "audio", nil)
	p := mustProduct(t, "AUD-1", cat.ID)
	before := len(readOutbox(t))

	dup := domain.Product{SKU: "AUD-1", Name: "Outro", CategoryID: cat.ID, PriceCents: 1, Status: domain.ProductActive}
	require.ErrorIs(t, repo.Create(ctx, &dup), domain.ErrConflict)

	stale := p
	stale.PriceCents = 1
	require.ErrorIs(t, repo.Update(ctx, &stale, 99), domain.ErrVersionConflict)

	missing := p
	missing.ID = uuid.New()
	require.ErrorIs(t, repo.Update(ctx, &missing, 1), domain.ErrNotFound)
	require.ErrorIs(t, repo.Delete(ctx, uuid.New()), domain.ErrNotFound)

	assert.Len(t, readOutbox(t), before, "escrita que falha não deixa evento para trás")

	got, err := repo.GetByID(ctx, p.ID, now)
	require.NoError(t, err)
	assert.Equal(t, 1, got.Version)
}

func TestCampaignWrites_VersionAndEvents(t *testing.T) {
	resetDB(t)
	repo := NewCampaignRepository(testDB)
	cat := mustCategory(t, "audio", nil)
	p1, p2 := mustProduct(t, "AUD-1", cat.ID), mustProduct(t, "AUD-2", cat.ID)
	require.NoError(t, testDB.Exec("DELETE FROM outbox").Error) // só os eventos de campanha

	c := domain.Campaign{Slug: "bf", Name: "Black Friday", DiscountPercent: 30, StartsAt: now, EndsAt: now.AddDate(0, 0, 3)}
	require.NoError(t, repo.Create(ctx, &c))
	assert.Equal(t, 1, c.Version)

	require.NoError(t, repo.SetProducts(ctx, c.ID, []uuid.UUID{p2.ID, p1.ID}))

	c.DiscountPercent = 40
	require.NoError(t, repo.Update(ctx, &c))
	assert.Equal(t, 3, c.Version, "Update devolve a versão nova (RETURNING)")

	stored, err := repo.GetByID(ctx, c.ID)
	require.NoError(t, err)
	assert.Equal(t, 3, stored.Version)

	require.NoError(t, repo.Delete(ctx, c.ID))

	evs := readOutbox(t)
	require.Equal(t, []string{
		events.TypeCampaignCreated, events.TypeCampaignUpdated, events.TypeCampaignUpdated, events.TypeCampaignDeleted,
	}, types(evs))
	for i, e := range evs {
		assert.Equal(t, events.TopicCampaigns, e.Topic)
		assert.Equal(t, c.ID.String(), e.Key)
		assert.InDelta(t, i+1, e.Data["version"], 0, "evento %d (%s)", i, e.Type)
	}
	assert.Empty(t, evs[0].Data["product_ids"])
	assert.Len(t, evs[1].Data["product_ids"], 2, "troca de produtos leva a lista nova")
	assert.Len(t, evs[2].Data["product_ids"], 2, "alteração de dados leva a lista atual")
	assert.InDelta(t, 40, evs[2].Data["discount_percent"], 0)
}

func TestCampaignWrites_NotFoundEnqueuesNothing(t *testing.T) {
	resetDB(t)
	repo := NewCampaignRepository(testDB)

	c := domain.Campaign{ID: uuid.New(), Slug: "x", Name: "X", DiscountPercent: 10, StartsAt: now, EndsAt: now.Add(1)}
	require.ErrorIs(t, repo.Update(ctx, &c), domain.ErrNotFound)
	require.ErrorIs(t, repo.Delete(ctx, c.ID), domain.ErrNotFound)
	require.ErrorIs(t, repo.SetProducts(ctx, c.ID, nil), domain.ErrNotFound)

	assert.Empty(t, readOutbox(t))
}

func seedOutbox(t *testing.T, n int) []uuid.UUID {
	t.Helper()
	cat := mustCategory(t, "audio", nil)
	require.NoError(t, testDB.Exec("DELETE FROM outbox").Error)
	repo := NewProductRepository(testDB)
	ids := make([]uuid.UUID, n)
	for i := range n {
		p := domain.Product{SKU: uuid.NewString(), Name: "P", CategoryID: cat.ID, PriceCents: 100, Status: domain.ProductActive}
		require.NoError(t, repo.Create(ctx, &p))
		ids[i] = p.ID
	}
	return ids
}

func TestDrain_PublishesInOrderAndDeletes(t *testing.T) {
	resetDB(t)
	ids := seedOutbox(t, 5)
	outbox := NewOutboxRepository(testDB)

	var got []string
	publish := func(_ context.Context, msgs []events.Message) error {
		for _, m := range msgs {
			got = append(got, m.Key)
		}
		return nil
	}

	n, err := outbox.Drain(ctx, 3, publish)
	require.NoError(t, err)
	assert.Equal(t, 3, n)
	n, err = outbox.Drain(ctx, 3, publish)
	require.NoError(t, err)
	assert.Equal(t, 2, n)
	n, err = outbox.Drain(ctx, 3, publish)
	require.NoError(t, err)
	assert.Equal(t, 0, n)

	want := make([]string, len(ids))
	for i, id := range ids {
		want[i] = id.String()
	}
	assert.Equal(t, want, got, "na ordem em que foram gravados")
	assert.Empty(t, readOutbox(t))
}

func TestDrain_PublishFailureKeepsEvents(t *testing.T) {
	resetDB(t)
	seedOutbox(t, 2)
	outbox := NewOutboxRepository(testDB)
	brokerDown := errors.New("broker fora")

	n, err := outbox.Drain(ctx, 10, func(context.Context, []events.Message) error { return brokerDown })
	require.ErrorIs(t, err, brokerDown)
	assert.Equal(t, 0, n)
	assert.Len(t, readOutbox(t), 2)

	var sent []events.Message
	n, err = outbox.Drain(ctx, 10, func(_ context.Context, msgs []events.Message) error { sent = msgs; return nil })
	require.NoError(t, err)
	assert.Equal(t, 2, n)
	require.Len(t, sent, 2)
	assert.Equal(t, events.TypeProductCreated, sent[0].Type)
	assert.NotEmpty(t, sent[0].Payload)
	assert.NotNil(t, sent[0].Headers)
}

func TestDrain_OnlyOneReplicaAtATime(t *testing.T) {
	resetDB(t)
	seedOutbox(t, 1)
	outbox := NewOutboxRepository(testDB)

	// outra "réplica" segura o lock numa transação aberta
	require.NoError(t, testDB.Transaction(func(tx *gorm.DB) error {
		var locked bool
		require.NoError(t, tx.Raw("SELECT pg_try_advisory_xact_lock(?)", relayLockKey).Scan(&locked).Error)
		require.True(t, locked)

		called := false
		n, err := outbox.Drain(ctx, 10, func(context.Context, []events.Message) error { called = true; return nil })
		require.NoError(t, err)
		assert.Equal(t, 0, n)
		assert.False(t, called, "sem o lock, não publica")
		return nil
	}))

	n, err := outbox.Drain(ctx, 10, func(context.Context, []events.Message) error { return nil })
	require.NoError(t, err)
	assert.Equal(t, 1, n, "lock liberado: publica")
}
