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
	assert.Equal(t, 30*time.Minute, cfg.DB.ConnMaxLifetime)
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
		"porta fora do intervalo": {"CATALOG_HTTP_PORT": "70000"},
		"nível de log":            {"CATALOG_LOG_LEVEL": "verbose"},
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
