package config

import (
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func TestLoad_DefaultsAndOverrides(t *testing.T) {
	c, err := load(func(string) string { return "" })
	require.NoError(t, err)
	assert.Equal(t, 8090, c.Port)
	assert.Equal(t, []string{"5120", "6120"}, c.AllowedCFOP)
	assert.Equal(t, 24*time.Hour, c.CancelWindow)

	env := map[string]string{"SIM_ALLOWED_CFOP": "5949, 6949", "SIM_TOKEN_TTL": "10m"}
	c, err = load(func(k string) string { return env[k] })
	require.NoError(t, err)
	assert.Equal(t, []string{"5949", "6949"}, c.AllowedCFOP)
	assert.Equal(t, 10*time.Minute, c.TokenTTL)

	_, err = load(func(k string) string { return map[string]string{"PORT": "x"}[k] })
	require.Error(t, err)
}
