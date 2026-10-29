package freight

import (
	"testing"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func TestQuote_TwoServices_ByRegionAndWeight(t *testing.T) {
	sp, err := Quote("BR", "01310-100", []Item{{"SIM-V-001", 1}})
	require.NoError(t, err)
	require.Len(t, sp, 2)
	assert.Equal(t, "CJPacket Ordinary", sp[0].LogisticName)
	assert.Equal(t, "12-20", sp[0].LogisticAging)
	assert.Equal(t, "6-10", sp[1].LogisticAging)
	assert.Less(t, sp[0].LogisticPrice, sp[1].LogisticPrice, "o expresso custa mais")

	north, err := Quote("BR", "69005000", []Item{{"SIM-V-001", 1}})
	require.NoError(t, err)
	assert.Equal(t, "17-25", north[0].LogisticAging, "Manaus demora mais")
	assert.Greater(t, north[0].LogisticPrice, sp[0].LogisticPrice)

	heavy, err := Quote("BR", "01310100", []Item{{"SIM-V-001", 10}})
	require.NoError(t, err)
	assert.Greater(t, heavy[0].LogisticPrice, sp[0].LogisticPrice, "mais peso, frete maior")

	again, err := Quote("BR", "01310100", []Item{{"SIM-V-001", 1}})
	require.NoError(t, err)
	assert.Equal(t, sp, again, "determinístico")
}

func TestQuote_Invalid(t *testing.T) {
	for _, tc := range []struct {
		country, zip string
		items        []Item
	}{
		{"US", "01310100", []Item{{"SIM-V-001", 1}}},
		{"BR", "0131", []Item{{"SIM-V-001", 1}}},
		{"BR", "01310100", nil},
		{"BR", "01310100", []Item{{"", 1}}},
	} {
		_, err := Quote(tc.country, tc.zip, tc.items)
		assert.ErrorIs(t, err, ErrInvalid)
	}
}
