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
	assert.Equal(t, DevJWTSecret, cfg.Auth.JWTSecret)
	assert.Equal(t, "catalog", cfg.Auth.Audience)
	assert.True(t, cfg.Cache.Enabled)
	assert.Equal(t, 5*time.Minute, cfg.Cache.ProductTTL)
	assert.Equal(t, "localhost:6379", cfg.Cache.Redis.Addr)
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
		"porta fora do intervalo":      {"CATALOG_HTTP_PORT": "70000"},
		"nível de log":                 {"CATALOG_LOG_LEVEL": "verbose"},
		"grpc na porta do http":        {"CATALOG_GRPC_PORT": "8001"},
		"segredo jwt curto":            {"CATALOG_AUTH_JWT_SECRET": "curto"},
		"segredo de dev fora do local": {"CATALOG_APP_ENV": "production"},
		"timeout do redis zerado":      {"CATALOG_CACHE_REDIS_TIMEOUT": "0s"},
		"ttl do cache zerado":          {"CATALOG_CACHE_LIST_TTL": "0s"},
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
