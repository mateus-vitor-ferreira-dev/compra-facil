package domain

import (
	"testing"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func TestSalePriceCents(t *testing.T) {
	rates := ExchangeRates{"USD": 5400}
	rule := PricingRule{MarginPercent: 80, MinPriceCents: 1990}

	// US$ 12,40 → R$ 66,96 → +80% = R$ 120,53 → R$ 120,90
	price, err := SalePriceCents(1240, "USD", rates, rule)
	require.NoError(t, err)
	assert.Equal(t, int64(12090), price)

	// já termina em ,90: fica
	price, err = SalePriceCents(6690, "BRL", rates, PricingRule{})
	require.NoError(t, err)
	assert.Equal(t, int64(6690), price)

	// barato demais: vale o mínimo (também terminando em ,90)
	price, err = SalePriceCents(190, "USD", rates, PricingRule{MarginPercent: 50, MinPriceCents: 2000})
	require.NoError(t, err)
	assert.Equal(t, int64(2090), price)

	_, err = SalePriceCents(100, "EUR", rates, rule)
	require.Error(t, err, "moeda sem câmbio")
	_, err = SalePriceCents(0, "USD", rates, rule)
	require.Error(t, err)
}

func TestRoundUpToEnding(t *testing.T) {
	for in, want := range map[int64]int64{1: 90, 90: 90, 91: 190, 6696: 6790, 6790: 6790, 6791: 6890} {
		assert.Equal(t, want, roundUpToEnding(in), "de %d", in)
	}
}

func TestParseExchangeRate(t *testing.T) {
	for in, want := range map[string]int64{"5.40": 5400, "5": 5000, "5.4": 5400, "0.183": 183} {
		got, err := ParseExchangeRate(in)
		require.NoError(t, err, in)
		assert.Equal(t, want, got, in)
	}
	for _, bad := range []string{"", "abc", "5.4321", "0", "-1"} {
		_, err := ParseExchangeRate(bad)
		assert.Error(t, err, bad)
	}
}

func TestRuleFor(t *testing.T) {
	root, child, leaf, other := uuid.New(), uuid.New(), uuid.New(), uuid.New()
	parent := map[uuid.UUID]uuid.UUID{child: root, leaf: child}
	fallback := PricingRule{MarginPercent: 80}
	rootRule := PricingRule{CategoryID: &root, MarginPercent: 60}
	leafRule := PricingRule{CategoryID: &leaf, MarginPercent: 120}

	assert.Equal(t, 120, RuleFor(leaf, []PricingRule{rootRule, leafRule}, parent, fallback).MarginPercent)
	assert.Equal(t, 60, RuleFor(child, []PricingRule{rootRule, leafRule}, parent, fallback).MarginPercent, "herda do ancestral")
	assert.Equal(t, 80, RuleFor(other, []PricingRule{rootRule}, parent, fallback).MarginPercent, "sem regra no caminho")
	assert.Equal(t, 70, RuleFor(other, []PricingRule{{MarginPercent: 70}}, parent, fallback).MarginPercent, "a padrão do banco vence a da config")
}

func TestPricingRuleValidate(t *testing.T) {
	require.NoError(t, PricingRule{MarginPercent: 80, MinPriceCents: 1990}.Validate())
	require.Error(t, PricingRule{MarginPercent: -1}.Validate())
	require.Error(t, PricingRule{MarginPercent: 1001}.Validate())
	require.Error(t, PricingRule{MinPriceCents: -5}.Validate())
}
