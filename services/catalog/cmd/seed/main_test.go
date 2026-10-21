package main

import (
	"math/rand/v2"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func subsForTest() []seededSub {
	var subs []seededSub
	for _, root := range catalogTree {
		for _, sub := range root.children {
			subs = append(subs, seededSub{subcategory: sub, id: uuid.New(), rootSlug: root.slug})
		}
	}
	return subs
}

func TestGenerateProducts_DeterministicAndValid(t *testing.T) {
	now := time.Date(2026, 11, 27, 0, 0, 0, 0, time.UTC)
	subs := subsForTest()
	gen := func() []seedProduct { return generateProducts(rand.New(rand.NewPCG(42, 7)), subs, 500, now) }

	a, b := gen(), gen()
	require.Len(t, a, 500)
	assert.Equal(t, a, b, "mesma semente, mesmos produtos")

	skus := map[string]bool{}
	for _, p := range a {
		assert.False(t, skus[p.sku], "SKU repetido: %s", p.sku)
		skus[p.sku] = true
		assert.Regexp(t, `^[A-Z0-9][A-Z0-9-]{2,31}$`, p.sku, "SKU no formato do domínio")
		assert.Equal(t, int64(90), p.price%100, "preço termina em ,90")
		assert.Contains(t, []string{"active", "inactive", "draft"}, p.status)
		assert.Equal(t, uuid.Version(4), p.id.Version())
	}
}

func TestRoundPrice(t *testing.T) {
	assert.Equal(t, int64(123490), roundPrice(123456))
	assert.Equal(t, int64(90), roundPrice(50), "nunca zero ou negativo")
}
