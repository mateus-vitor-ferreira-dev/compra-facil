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
	assert.Equal(t, 5*time.Second, c.EfiSettleDelay)
	assert.Equal(t, "http://localhost:8092", c.PublicURL)

	env := map[string]string{"SIM_EFI_SETTLE_DELAY": "2m", "SIM_PUBLIC_URL": "http://sim:9/", "PORT": "9090"}
	c, err = load(func(k string) string { return env[k] })
	require.NoError(t, err)
	assert.Equal(t, (2 * time.Minute), c.EfiSettleDelay)
	assert.Equal(t, "http://sim:9", c.PublicURL)
	assert.Equal(t, 9090, c.Port)

	_, err = load(func(k string) string { return map[string]string{"SIM_EFI_SETTLE_DELAY": "nunca"}[k] })
	require.Error(t, err)
}
