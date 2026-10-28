package catalog

import (
	"testing"

	"github.com/stretchr/testify/assert"
)

func TestPage(t *testing.T) {
	first, total := Page(1, 20)
	assert.Equal(t, 30, total)
	assert.Len(t, first, 20)
	assert.Equal(t, "SIM-V-001", first[0].VID)
	assert.Equal(t, "12.40", first[0].SellPrice)

	last, _ := Page(2, 20)
	assert.Len(t, last, 10)

	empty, _ := Page(5, 20)
	assert.Empty(t, empty)
}

func TestStockDeterministic(t *testing.T) {
	s := NewStock(map[string]bool{"ESGOTADO": true})
	for _, vid := range []string{"SIM-V-001", "SKU-DA-LOJA-123", "x"} {
		n := s.Available(vid)
		assert.Equal(t, n, s.Available(vid))
		assert.GreaterOrEqual(t, n, 5)
		assert.LessOrEqual(t, n, 200)
	}
	assert.Zero(t, s.Available("ESGOTADO"))
}

func TestUnitCost(t *testing.T) {
	assert.Equal(t, int64(1240), UnitCostCents("SIM-V-001"))
	c := UnitCostCents("SKU-DA-LOJA-123")
	assert.Equal(t, c, UnitCostCents("SKU-DA-LOJA-123"))
	assert.GreaterOrEqual(t, c, int64(300))
	assert.LessOrEqual(t, c, int64(6000))
	assert.Equal(t, "0.05", FormatUSD(5))
	assert.Equal(t, "120.00", FormatUSD(12000))
}
