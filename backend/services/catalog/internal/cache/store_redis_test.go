package cache

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"github.com/testcontainers/testcontainers-go"
	"github.com/testcontainers/testcontainers-go/wait"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

// redisImage é a mesma do compose (infra/compose/compose.yaml).
const redisImage = "redis:8.8.3-alpine"

// TestRedisStore_Integration roda o cache contra um Redis de verdade: os outros testes do pacote usam
// o memStore, que não pega diferença de protocolo, TTL nem timeout do go-redis.
func TestRedisStore_Integration(t *testing.T) {
	if testing.Short() {
		t.Skip("integração com Redis pulada (-short)")
	}
	ctx := context.Background()
	ctr, err := testcontainers.Run(ctx, redisImage,
		testcontainers.WithExposedPorts("6379/tcp"),
		testcontainers.WithWaitStrategy(wait.ForListeningPort("6379/tcp")),
	)
	testcontainers.CleanupContainer(t, ctr)
	require.NoError(t, err)
	addr, err := ctr.PortEndpoint(ctx, "6379/tcp", "")
	require.NoError(t, err)

	store := NewRedisStore(RedisOptions{Addr: addr, DialTimeout: time.Second, ReadTimeout: time.Second, WriteTimeout: time.Second})
	t.Cleanup(func() { _ = store.Close() })
	require.NoError(t, store.Ping(ctx))

	t.Run("chave ausente é ErrMiss", func(t *testing.T) {
		_, err := store.Get(ctx, "nao-existe-"+uuid.NewString())
		require.ErrorIs(t, err, ErrMiss)
	})

	t.Run("Set e Get, e o TTL expira a chave", func(t *testing.T) {
		key := "ttl-" + uuid.NewString()
		require.NoError(t, store.Set(ctx, key, []byte("valor"), time.Second))
		got, err := store.Get(ctx, key)
		require.NoError(t, err)
		assert.Equal(t, []byte("valor"), got)

		require.Eventually(t, func() bool {
			_, err := store.Get(ctx, key)
			return errors.Is(err, ErrMiss)
		}, 3*time.Second, 100*time.Millisecond, "a chave expira pelo TTL")
	})

	t.Run("Incr conta a partir de 1", func(t *testing.T) {
		key := "gen-" + uuid.NewString()
		for want := int64(1); want <= 3; want++ {
			n, err := store.Incr(ctx, key)
			require.NoError(t, err)
			assert.Equal(t, want, n)
		}
	})

	t.Run("catálogo: miss, hit e invalidação contra o Redis", func(t *testing.T) {
		r := &fakeReaders{product: domain.Product{ID: uuid.New(), Name: "Fone", PriceCents: 19990, Currency: "BRL"}}
		c := newTestCatalog(t, store, r)

		first, err := c.GetPublic(ctx, r.product.ID)
		require.NoError(t, err)
		second, err := c.GetPublic(ctx, r.product.ID)
		require.NoError(t, err)
		assert.Equal(t, r.product, first)
		assert.Equal(t, first, second, "o valor volta igual depois de passar pelo JSON do Redis")
		assert.EqualValues(t, 1, r.getCalls.Load(), "a segunda leitura vem do Redis")

		c.Invalidate(ctx) // nova geração: as chaves antigas deixam de valer
		_, err = c.GetPublic(ctx, r.product.ID)
		require.NoError(t, err)
		assert.EqualValues(t, 2, r.getCalls.Load())
	})
}

// TestRedisStore_Unreachable confere o caminho real do Redis fora: o go-redis falha dentro do timeout
// configurado (sem as novas tentativas padrão) e a leitura cai no banco.
func TestRedisStore_Unreachable(t *testing.T) {
	store := NewRedisStore(RedisOptions{Addr: "127.0.0.1:1", DialTimeout: 200 * time.Millisecond,
		ReadTimeout: 200 * time.Millisecond, WriteTimeout: 200 * time.Millisecond})
	t.Cleanup(func() { _ = store.Close() })
	r := &fakeReaders{product: domain.Product{ID: uuid.New()}}
	c := newTestCatalog(t, store, r)

	started := time.Now()
	got, err := c.GetPublic(context.Background(), r.product.ID)
	require.NoError(t, err)
	assert.Equal(t, r.product, got)
	assert.Less(t, time.Since(started), 2*time.Second, "Redis fora não segura a leitura")
}
