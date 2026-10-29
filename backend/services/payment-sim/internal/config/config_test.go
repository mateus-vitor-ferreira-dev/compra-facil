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
	assert.Equal(t, 30*time.Minute, c.PIXExpiration)
	assert.Equal(t, "http://localhost:8092", c.PublicURL)

	env := map[string]string{"SIM_PIX_EXPIRATION": "2m", "SIM_PUBLIC_URL": "http://sim:9/", "PORT": "9090"}
	c, err = load(func(k string) string { return env[k] })
	require.NoError(t, err)
	assert.Equal(t, (2 * time.Minute), c.PIXExpiration)
	assert.Equal(t, "http://sim:9", c.PublicURL)
	assert.Equal(t, 9090, c.Port)

	_, err = load(func(k string) string { return map[string]string{"SIM_PIX_EXPIRATION": "nunca"}[k] })
	require.Error(t, err)
}
