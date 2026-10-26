package config

import (
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func TestLoad_Defaults(t *testing.T) {
	cfg, err := Load(t.TempDir())
	require.NoError(t, err)

	assert.Equal(t, "catalog", cfg.App.Name)
	assert.Equal(t, 8001, cfg.HTTP.Port)
	assert.Equal(t, ":8001", cfg.HTTP.Addr())
	assert.Equal(t, 15*time.Second, cfg.HTTP.ShutdownTimeout)
	assert.Equal(t, "info", cfg.Log.Level)
	assert.Contains(t, cfg.DB.DSN, "localhost:55432/catalog")
	assert.True(t, cfg.DB.MigrateOnStart)
	assert.Equal(t, ":9001", cfg.GRPC.Addr())
	assert.True(t, cfg.GRPC.Reflection)
	assert.Equal(t, 30*time.Minute, cfg.DB.ConnMaxLifetime)
	assert.Equal(t, "http://localhost:8006", cfg.Auth.Authority)
	assert.Equal(t, "compra-facil-identity", cfg.Auth.Issuer)
	assert.Equal(t, "compra-facil", cfg.Auth.Audience)
	assert.True(t, cfg.Cache.Enabled)
	assert.Equal(t, 5*time.Minute, cfg.Cache.ProductTTL)
	assert.Equal(t, "localhost:6379", cfg.Cache.Redis.Addr)
	assert.True(t, cfg.Events.RelayEnabled)
	assert.Equal(t, []string{"localhost:19092"}, cfg.Events.Brokers)
	assert.Equal(t, 100, cfg.Events.BatchSize)
}

func TestLoad_BrokersFromEnv(t *testing.T) {
	t.Setenv("CATALOG_EVENTS_BROKERS", "redpanda-0:9092,redpanda-1:9092")

	cfg, err := Load(t.TempDir())
	require.NoError(t, err)

	assert.Equal(t, []string{"redpanda-0:9092", "redpanda-1:9092"}, cfg.Events.Brokers)
}

func TestLoad_FileAndEnvPrecedence(t *testing.T) {
	dir := t.TempDir()
	yaml := "http:\n  port: 9000\n  read_timeout: 2s\nlog:\n  level: debug\n"
	require.NoError(t, os.WriteFile(filepath.Join(dir, "config.yaml"), []byte(yaml), 0o600))
	t.Setenv("CATALOG_HTTP_PORT", "9100")

	cfg, err := Load(dir)
	require.NoError(t, err)

	assert.Equal(t, 9100, cfg.HTTP.Port, "env vence o arquivo")
	assert.Equal(t, 2*time.Second, cfg.HTTP.ReadTimeout, "arquivo vence o default")
	assert.Equal(t, "debug", cfg.Log.Level)
}

func TestLoad_Invalid(t *testing.T) {
	tests := map[string]map[string]string{
		"porta fora do intervalo":       {"CATALOG_HTTP_PORT": "70000"},
		"nível de log":                  {"CATALOG_LOG_LEVEL": "verbose"},
		"grpc na porta do http":         {"CATALOG_GRPC_PORT": "8001"},
		"authority sem esquema":         {"CATALOG_AUTH_AUTHORITY": "identity:8006"},
		"authority com outro esquema":   {"CATALOG_AUTH_AUTHORITY": "ftp://identity"},
		"timeout do redis zerado":       {"CATALOG_CACHE_REDIS_TIMEOUT": "0s"},
		"ttl do cache zerado":           {"CATALOG_CACHE_LIST_TTL": "0s"},
		"lote de eventos zerado":        {"CATALOG_EVENTS_BATCH_SIZE": "0"},
		"backoff menor que o intervalo": {"CATALOG_EVENTS_MAX_BACKOFF": "100ms"},
	}
	for name, env := range tests {
		t.Run(name, func(t *testing.T) {
			for k, v := range env {
				t.Setenv(k, v)
			}
			_, err := Load(t.TempDir())
			assert.Error(t, err)
		})
	}
}
