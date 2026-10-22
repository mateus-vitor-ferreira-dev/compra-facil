// Package cache aplica cache-aside (Redis) às leituras públicas do catálogo, que recebem o pico
// das campanhas. O gRPC (validação de itens do pedido) não passa por aqui: preço de checkout
// vem sempre do banco.
package cache

import (
	"context"
	"errors"
	"fmt"
	"time"

	"github.com/redis/go-redis/v9"
)

// ErrMiss indica chave ausente no cache.
var ErrMiss = errors.New("cache miss")

// Store é o mínimo do Redis que o cache usa (os testes trocam por um mapa em memória).
type Store interface {
	Get(ctx context.Context, key string) ([]byte, error)
	Set(ctx context.Context, key string, value []byte, ttl time.Duration) error
	Incr(ctx context.Context, key string) (int64, error)
}

type RedisOptions struct {
	Addr         string
	Password     string
	DB           int
	DialTimeout  time.Duration
	ReadTimeout  time.Duration
	WriteTimeout time.Duration
}

type RedisStore struct{ client *redis.Client }

// NewRedisStore não conecta na hora: se o Redis estiver fora, as leituras caem no banco.
// Sem novas tentativas: com o Redis fora, cada leitura paga no máximo um timeout antes do banco
// (os defaults do go-redis somam segundos de retry/backoff por requisição).
func NewRedisStore(opts RedisOptions) *RedisStore {
	return &RedisStore{client: redis.NewClient(&redis.Options{
		Addr:          opts.Addr,
		Password:      opts.Password,
		DB:            opts.DB,
		DialTimeout:   opts.DialTimeout,
		ReadTimeout:   opts.ReadTimeout,
		WriteTimeout:  opts.WriteTimeout,
		MaxRetries:    -1,
		DialerRetries: 1,
	})}
}

func (s *RedisStore) Get(ctx context.Context, key string) ([]byte, error) {
	b, err := s.client.Get(ctx, key).Bytes()
	if errors.Is(err, redis.Nil) {
		return nil, ErrMiss
	}
	if err != nil {
		return nil, fmt.Errorf("redis GET: %w", err)
	}
	return b, nil
}

func (s *RedisStore) Set(ctx context.Context, key string, value []byte, ttl time.Duration) error {
	if err := s.client.Set(ctx, key, value, ttl).Err(); err != nil {
		return fmt.Errorf("redis SET: %w", err)
	}
	return nil
}

func (s *RedisStore) Incr(ctx context.Context, key string) (int64, error) {
	n, err := s.client.Incr(ctx, key).Result()
	if err != nil {
		return 0, fmt.Errorf("redis INCR: %w", err)
	}
	return n, nil
}

func (s *RedisStore) Ping(ctx context.Context) error {
	if err := s.client.Ping(ctx).Err(); err != nil {
		return fmt.Errorf("redis PING: %w", err)
	}
	return nil
}

func (s *RedisStore) Close() error {
	if err := s.client.Close(); err != nil {
		return fmt.Errorf("fechando redis: %w", err)
	}
	return nil
}
