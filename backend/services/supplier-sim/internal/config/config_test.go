package config

import (
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func env(m map[string]string) func(string) string {
	return func(k string) string { return m[k] }
}

func TestLoadDefaults(t *testing.T) {
	c, err := load(env(nil))
	require.NoError(t, err)
	assert.Equal(t, 8090, c.Port)
	assert.Equal(t, "sim@comprafacil.local", c.Email)
	assert.Equal(t, 2*time.Second, c.AcceptDelay)
	assert.Equal(t, 5*time.Second, c.ShipDelay)
	assert.Equal(t, 10*time.Second, c.DeliverDelay)
	assert.Equal(t, "sim-secret", c.WebhookSecret)
	assert.Empty(t, c.WebhookURL)
	assert.False(t, c.WebhookDuplicate)
}

func TestLoadOverrides(t *testing.T) {
	c, err := load(env(map[string]string{
		"PORT":                  "9000",
		"SIM_ACCEPT_DELAY":      "10ms",
		"SIM_OUT_OF_STOCK_VIDS": " A , B,,",
		"SIM_WEBHOOK_DUPLICATE": "true",
	}))
	require.NoError(t, err)
	assert.Equal(t, 9000, c.Port)
	assert.Equal(t, 10*time.Millisecond, c.AcceptDelay)
	assert.Equal(t, map[string]bool{"A": true, "B": true}, c.OutOfStock)
	assert.True(t, c.WebhookDuplicate)
}

func TestLoadInvalid(t *testing.T) {
	for _, kv := range [][2]string{{"PORT", "x"}, {"SIM_SHIP_DELAY", "5"}, {"SIM_WEBHOOK_DUPLICATE", "talvez"}} {
		_, err := load(env(map[string]string{kv[0]: kv[1]}))
		assert.Error(t, err, kv[0])
	}
}
