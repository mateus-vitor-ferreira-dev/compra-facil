package domain

import (
	"errors"
	"fmt"
	"strconv"
	"strings"
	"time"

	"github.com/google/uuid"
)

// ErrDeleted indica que o produto existe, mas foi removido pela equipe (não volta pelo feed).
var ErrDeleted = errors.New("removido")

// SupplierInfo é o lado do fornecedor de um produto de dropshipping (ADR 0007): de onde ele vem e quanto
// custa. Só o painel vê; o preço de venda sai do custo pela regra de margem.
type SupplierInfo struct {
	Name string
	// VID é a variante no fornecedor (o que vai no pedido a ele); o SKU da loja é o mesmo.
	VID          string
	CostCents    int64
	CostCurrency string
	// SyncedAt é o updated_at do último evento do fornecedor aplicado: um mais velho não desfaz este.
	SyncedAt time.Time
	// RemovedAt: o produto saiu do feed (nulo enquanto o fornecedor vende).
	RemovedAt *time.Time
}

// PricingRule é a margem de uma categoria (e das descendentes sem regra própria); CategoryID nulo é a
// regra padrão, a de quem não tem nenhuma no caminho até a raiz.
type PricingRule struct {
	CategoryID    *uuid.UUID
	MarginPercent int
	MinPriceCents int64
	UpdatedAt     time.Time
}

const (
	MaxMarginPercent = 1000
	// os preços terminam em ,90 (R$ 67,90)
	priceEnding = 90
)

func (r PricingRule) Validate() error {
	var v validator
	v.check(r.MarginPercent >= 0 && r.MarginPercent <= MaxMarginPercent, "margin_percent", "deve estar entre 0 e %d", MaxMarginPercent)
	v.check(r.MinPriceCents >= 0 && r.MinPriceCents <= MaxPriceCents, "min_price_cents", "deve estar entre 0 e %d", MaxPriceCents)
	return v.err()
}

// ExchangeRates é quanto vale uma unidade de cada moeda em reais, em milésimos de real (5,40 → 5400):
// inteiros, para o preço não depender de arredondamento de ponto flutuante.
type ExchangeRates map[string]int64

// ParseExchangeRate lê "5.40" (reais por unidade) em milésimos.
func ParseExchangeRate(s string) (int64, error) {
	whole, frac, _ := strings.Cut(strings.TrimSpace(s), ".")
	if len(frac) > 3 {
		return 0, fmt.Errorf("câmbio %q: no máximo 3 casas decimais", s)
	}
	frac += strings.Repeat("0", 3-len(frac))
	w, err := strconv.ParseInt(whole, 10, 64)
	if err != nil {
		return 0, fmt.Errorf("câmbio %q: %w", s, err)
	}
	f, err := strconv.ParseInt(frac, 10, 64)
	if err != nil {
		return 0, fmt.Errorf("câmbio %q: %w", s, err)
	}
	rate := w*1000 + f
	if rate <= 0 {
		return 0, fmt.Errorf("câmbio %q: deve ser positivo", s)
	}
	return rate, nil
}

// SalePriceCents é o preço de venda em centavos de real: o custo convertido, mais a margem,
// arredondado para cima até terminar em ,90, nunca abaixo do mínimo da regra. Sempre para cima: a
// loja não vende abaixo da margem por arredondamento.
func SalePriceCents(costCents int64, currency string, rates ExchangeRates, rule PricingRule) (int64, error) {
	if costCents <= 0 {
		return 0, fmt.Errorf("custo inválido: %d", costCents)
	}
	brl := costCents
	if currency != DefaultCurrency {
		rate, ok := rates[currency]
		if !ok {
			return 0, fmt.Errorf("sem câmbio para %s", currency)
		}
		brl = ceilDiv(costCents*rate, 1000)
	}
	price := ceilDiv(brl*int64(100+rule.MarginPercent), 100)
	price = roundUpToEnding(max(price, rule.MinPriceCents))
	if price > MaxPriceCents {
		return 0, fmt.Errorf("preço calculado acima do máximo: %d", price)
	}
	return price, nil
}

// roundUpToEnding devolve o menor valor >= cents que termina em ,90.
func roundUpToEnding(cents int64) int64 {
	reais := ceilDiv(cents-priceEnding, 100)
	return max(reais, 0)*100 + priceEnding
}

func ceilDiv(a, b int64) int64 {
	if a <= 0 {
		return a / b
	}
	return (a + b - 1) / b
}

// RuleFor escolhe a regra de uma categoria: a dela, ou a do ancestral mais próximo, ou a padrão.
// parent leva cada categoria ao pai.
func RuleFor(categoryID uuid.UUID, rules []PricingRule, parent map[uuid.UUID]uuid.UUID, fallback PricingRule) PricingRule {
	byCategory := map[uuid.UUID]PricingRule{}
	for _, r := range rules {
		if r.CategoryID == nil {
			fallback = r
		} else {
			byCategory[*r.CategoryID] = r
		}
	}
	for id, depth := categoryID, 0; depth < 16; depth++ {
		if r, ok := byCategory[id]; ok {
			return r
		}
		p, ok := parent[id]
		if !ok {
			break
		}
		id = p
	}
	return fallback
}
