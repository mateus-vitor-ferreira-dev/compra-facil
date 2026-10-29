package messaging

import (
	"encoding/json"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func TestTime_ReadsTheDotNetEscapedOffset(t *testing.T) {
	var v struct {
		At Time `json:"at"`
	}
	require.NoError(t, json.Unmarshal([]byte(`{"at":"2026-10-29T13:48:57.202754+00:00"}`), &v))
	assert.True(t, v.At.Equal(time.Date(2026, 10, 29, 13, 48, 57, 202754000, time.UTC)))

	require.Error(t, json.Unmarshal([]byte(`{"at":"ontem"}`), &v))
	require.Error(t, json.Unmarshal([]byte(`{"at":123}`), &v))
}
