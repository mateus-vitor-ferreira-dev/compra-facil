package cache

import (
	"context"
	"errors"
	"log/slog"
	"strconv"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/service"
)

// memStore é um Redis de mentira: guarda valor e TTL, e pode simular indisponibilidade.
type memStore struct {
	mu   sync.Mutex
	data map[string][]byte
	ttls map[string]time.Duration
	down bool
}

func newMemStore() *memStore {
	return &memStore{data: map[string][]byte{}, ttls: map[string]time.Duration{}}
}

var errDown = errors.New("redis fora")

func (s *memStore) Get(_ context.Context, key string) ([]byte, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.down {
		return nil, errDown
	}
	b, ok := s.data[key]
	if !ok {
		return nil, ErrMiss
	}
	return b, nil
}

func (s *memStore) Set(_ context.Context, key string, value []byte, ttl time.Duration) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.down {
		return errDown
	}
	s.data[key], s.ttls[key] = value, ttl
	return nil
}

func (s *memStore) Incr(_ context.Context, key string) (int64, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.down {
		return 0, errDown
	}
	n, _ := strconv.ParseInt(string(s.data[key]), 10, 64)
	n++
	s.data[key] = []byte(strconv.FormatInt(n, 10))
	return n, nil
}

func (s *memStore) ttlOf(prefix string) (time.Duration, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	for k, ttl := range s.ttls {
		if len(k) >= len(prefix) && k[:len(prefix)] == prefix {
			return ttl, true
		}
	}
	return 0, false
}

// fakeReaders conta as idas ao "banco".
type fakeReaders struct {
	product   domain.Product
	page      domain.Page[domain.Product]
	campaigns []domain.Campaign
	err       error
	delay     time.Duration
	getCalls  atomic.Int32
	listCalls atomic.Int32
	catCalls  atomic.Int32
	campCalls atomic.Int32
}

func (f *fakeReaders) GetPublic(context.Context, uuid.UUID) (domain.Product, error) {
	f.getCalls.Add(1)
	time.Sleep(f.delay)
	return f.product, f.err
}

func (f *fakeReaders) ListPublic(context.Context, service.ListProductsInput) (domain.Page[domain.Product], error) {
	f.listCalls.Add(1)
	return f.page, f.err
}

func (f *fakeReaders) List(context.Context) ([]domain.Category, error) {
	f.catCalls.Add(1)
	return []domain.Category{{Name: "Eletrônicos", Slug: "eletronicos"}}, f.err
}

func (f *fakeReaders) ListActive(context.Context) ([]domain.Campaign, error) {
	f.campCalls.Add(1)
	return f.campaigns, f.err
}

var now = time.Date(2026, 11, 1, 12, 0, 0, 0, time.UTC)

func newTestCatalog(t *testing.T, store Store, r *fakeReaders) *Catalog {
	t.Helper()
	c, err := NewCatalog(store, r, r, r, Options{ProductTTL: 5 * time.Minute, ListTTL: time.Minute},
		func() time.Time { return now }, slog.New(slog.DiscardHandler))
	require.NoError(t, err)
	return c
}

func TestGetPublic_MissThenHit(t *testing.T) {
	store := newMemStore()
	r := &fakeReaders{product: domain.Product{ID: uuid.New(), Name: "Fone"}}
	c := newTestCatalog(t, store, r)
	ctx := context.Background()

	first, err := c.GetPublic(ctx, r.product.ID)
	require.NoError(t, err)
	second, err := c.GetPublic(ctx, r.product.ID)
	require.NoError(t, err)

	assert.Equal(t, r.product, first)
	assert.Equal(t, first, second)
	assert.EqualValues(t, 1, r.getCalls.Load(), "a segunda leitura vem do cache")
}

func TestInvalidate_NewGenerationReloads(t *testing.T) {
	store := newMemStore()
	r := &fakeReaders{}
	c := newTestCatalog(t, store, r)
	ctx := context.Background()

	_, err := c.List(ctx)
	require.NoError(t, err)
	c.Invalidate(ctx)
	_, err = c.List(ctx)
	require.NoError(t, err)

	assert.EqualValues(t, 2, r.catCalls.Load())
}

func TestListPublic_DistinctFiltersDistinctKeys(t *testing.T) {
	r := &fakeReaders{}
	c := newTestCatalog(t, newMemStore(), r)
	ctx := context.Background()

	for range 2 {
		_, _ = c.ListPublic(ctx, service.ListProductsInput{CategorySlug: "a"})
		_, _ = c.ListPublic(ctx, service.ListProductsInput{CategorySlug: "b"})
	}

	assert.EqualValues(t, 2, r.listCalls.Load())
}

func TestTTL_CappedByCampaignEnd(t *testing.T) {
	store := newMemStore()
	r := &fakeReaders{product: domain.Product{
		ID:             uuid.New(),
		ActiveCampaign: &domain.CampaignRef{EndsAt: now.Add(90 * time.Second)},
	}}
	c := newTestCatalog(t, store, r)

	_, err := c.GetPublic(context.Background(), r.product.ID)
	require.NoError(t, err)

	ttl, ok := store.ttlOf(keyPrefix + "0:product:")
	require.True(t, ok)
	assert.Equal(t, 90*time.Second, ttl, "expira junto com a campanha, não em ProductTTL")
}

func TestTTL_ExpiredCampaignIsNotCached(t *testing.T) {
	store := newMemStore()
	r := &fakeReaders{campaigns: []domain.Campaign{{EndsAt: now.Add(-time.Second)}}}
	c := newTestCatalog(t, store, r)
	ctx := context.Background()

	_, _ = c.ListActive(ctx)
	_, _ = c.ListActive(ctx)

	_, ok := store.ttlOf(keyPrefix + "0:campaigns")
	assert.False(t, ok)
	assert.EqualValues(t, 2, r.campCalls.Load())
}

func TestRedisDown_FallsBackToDatabase(t *testing.T) {
	store := newMemStore()
	store.down = true
	r := &fakeReaders{product: domain.Product{ID: uuid.New()}}
	c := newTestCatalog(t, store, r)
	ctx := context.Background()

	got, err := c.GetPublic(ctx, r.product.ID)
	require.NoError(t, err)
	assert.Equal(t, r.product, got)
	c.Invalidate(ctx) // não entra em pânico nem propaga erro
}

func TestLoadError_IsReturnedAndNotCached(t *testing.T) {
	store := newMemStore()
	r := &fakeReaders{err: domain.ErrNotFound}
	c := newTestCatalog(t, store, r)
	ctx := context.Background()

	_, err := c.GetPublic(ctx, uuid.New())
	require.ErrorIs(t, err, domain.ErrNotFound)
	_, ok := store.ttlOf(keyPrefix)
	assert.False(t, ok)
}

func TestCorruptedEntry_IsReloaded(t *testing.T) {
	store := newMemStore()
	r := &fakeReaders{}
	c := newTestCatalog(t, store, r)
	require.NoError(t, store.Set(context.Background(), keyPrefix+"0:categories", []byte("{lixo"), time.Minute))

	cats, err := c.List(context.Background())
	require.NoError(t, err)
	assert.Len(t, cats, 1)
	assert.EqualValues(t, 1, r.catCalls.Load())
}

func TestConcurrentMisses_LoadOnce(t *testing.T) {
	r := &fakeReaders{product: domain.Product{ID: uuid.New()}, delay: 50 * time.Millisecond}
	c := newTestCatalog(t, newMemStore(), r)

	var wg sync.WaitGroup
	for range 20 {
		wg.Go(func() {
			_, err := c.GetPublic(context.Background(), r.product.ID)
			assert.NoError(t, err)
		})
	}
	wg.Wait()

	assert.EqualValues(t, 1, r.getCalls.Load(), "singleflight: uma ida ao banco por chave")
}

type invalidations struct{ n atomic.Int32 }

func (i *invalidations) Invalidate(context.Context) { i.n.Add(1) }

func TestInvalidateErr_OnlyOnSuccess(t *testing.T) {
	inv := &invalidations{}
	ctx := context.Background()

	require.NoError(t, invalidateErr(ctx, inv, nil))
	require.Error(t, invalidateErr(ctx, inv, errors.New("conflito")))
	_, err := invalidateOnSuccess(ctx, inv, 0, errors.New("validação"))
	require.Error(t, err)

	assert.EqualValues(t, 1, inv.n.Load())
}
