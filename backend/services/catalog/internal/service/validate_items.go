package service

import (
	"context"
	"fmt"
	"strings"
	"time"
	"unicode/utf8"

	"github.com/google/uuid"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

const (
	MaxOrderItems   = 50
	MinItemQuantity = 1
	MaxItemQuantity = 99
)

// ItemStatus espelha catalog.v1.ItemStatus.
type ItemStatus int

const (
	ItemOK ItemStatus = iota + 1
	ItemNotFound
	ItemNotActive
	ItemPriceChanged
	ItemInvalidQuantity
	ItemInvalidSize
	ItemInvalidPersonalization
	ItemInvalidColor
)

type ItemToValidate struct {
	ProductID uuid.UUID
	Quantity  int
	// ExpectedUnitPriceCents é o preço que o cliente viu; nil não confere.
	ExpectedUnitPriceCents *int64
	// Size é o tamanho escolhido; vazio no produto sem tamanho.
	Size string
	// Personalization é o texto do cliente (ex.: o nome na estampa); vazio no produto sem personalização.
	Personalization string
	// Color é a cor escolhida; vazio no produto sem cor à escolha.
	Color string
}

type ValidatedItem struct {
	ProductID uuid.UUID
	Status    ItemStatus
	Quantity  int
	// Size é o tamanho como está no cadastro (ex.: "g" pedido volta "G").
	Size string
	// Personalization é o texto normalizado (sem espaços nas pontas nem repetidos), como vai para a arte.
	Personalization string
	// Color é a cor como está no cadastro (ex.: "preto" pedido volta "Preto").
	Color string
	// Product vem preenchido sempre que o produto existe (inclusive com status de erro).
	Product        *domain.Product
	UnitPriceCents int64
	LineTotalCents int64
}

type ItemsValidation struct {
	Valid      bool
	Items      []ValidatedItem // mesma ordem da entrada
	TotalCents int64           // soma dos itens OK
	Currency   string
	PricedAt   time.Time
}

// ValidateItems confere se os itens de um pedido podem ser vendidos agora e calcula o preço
// efetivo. Usa um único instante (PricedAt) para todas as campanhas, para o total ser coerente.
func (s *ProductService) ValidateItems(ctx context.Context, items []ItemToValidate) (ItemsValidation, error) {
	if err := validateItemsRequest(items); err != nil {
		return ItemsValidation{}, err
	}

	pricedAt := s.now()
	ids := make([]uuid.UUID, len(items))
	for i, it := range items {
		ids[i] = it.ProductID
	}
	found, err := s.products.GetByIDs(ctx, ids, pricedAt)
	if err != nil {
		return ItemsValidation{}, fmt.Errorf("buscando produtos do pedido: %w", err)
	}
	byID := make(map[uuid.UUID]domain.Product, len(found))
	for _, p := range found {
		byID[p.ID] = p
	}

	out := ItemsValidation{Valid: true, Items: make([]ValidatedItem, len(items)), Currency: domain.DefaultCurrency, PricedAt: pricedAt}
	for i, it := range items {
		v := validateItem(it, byID)
		out.Items[i] = v
		if v.Status != ItemOK {
			out.Valid = false
			continue
		}
		out.TotalCents += v.LineTotalCents
		out.Currency = v.Product.Currency
	}
	return out, nil
}

func validateItem(it ItemToValidate, byID map[uuid.UUID]domain.Product) ValidatedItem {
	text := domain.NormalizePersonalizationText(it.Personalization)
	v := ValidatedItem{ProductID: it.ProductID, Quantity: it.Quantity, Size: it.Size, Personalization: text}
	p, ok := byID[it.ProductID]
	if !ok {
		v.Status = ItemNotFound
		return v
	}
	v.Product = &p
	v.UnitPriceCents = p.EffectivePriceCents()
	v.Size = canonicalSize(p, it.Size)
	v.Color = canonicalIn(p.Colors, it.Color)

	switch {
	case p.Status != domain.ProductActive:
		v.Status = ItemNotActive
	case it.Quantity < MinItemQuantity || it.Quantity > MaxItemQuantity:
		v.Status = ItemInvalidQuantity
	case !p.HasSize(it.Size):
		v.Status = ItemInvalidSize
	case !p.HasColor(it.Color):
		v.Status = ItemInvalidColor
	case !p.AcceptsPersonalization(text):
		v.Status = ItemInvalidPersonalization
	case it.ExpectedUnitPriceCents != nil && *it.ExpectedUnitPriceCents != v.UnitPriceCents:
		v.Status = ItemPriceChanged
	default:
		v.Status = ItemOK
		v.LineTotalCents = v.UnitPriceCents * int64(it.Quantity)
	}
	return v
}

// canonicalIn devolve o valor como a lista o cadastra; um que ela não tem volta como veio.
func canonicalIn(list []string, value string) string {
	for _, s := range list {
		if strings.EqualFold(s, value) {
			return s
		}
	}
	return value
}

// canonicalSize devolve o tamanho como o produto o cadastra; um que ele não tem volta como veio.
func canonicalSize(p domain.Product, size string) string {
	for _, s := range p.Sizes {
		if strings.EqualFold(s, size) {
			return s
		}
	}
	return size
}

// validateItemsRequest recusa requisições malformadas (viram INVALID_ARGUMENT no gRPC).
func validateItemsRequest(items []ItemToValidate) error {
	if len(items) == 0 || len(items) > MaxOrderItems {
		return domain.Invalid("items", fmt.Sprintf("deve ter de 1 a %d itens", MaxOrderItems))
	}
	type line struct {
		id              uuid.UUID
		size            string
		color           string
		personalization string
	}
	seen := make(map[line]struct{}, len(items))
	for i, it := range items {
		field := fmt.Sprintf("items[%d].product_id", i)
		if it.ProductID == uuid.Nil {
			return domain.Invalid(field, "é obrigatório")
		}
		if len([]rune(it.Size)) > domain.MaxSizeLength {
			return domain.Invalid(fmt.Sprintf("items[%d].size", i), fmt.Sprintf("deve ter no máximo %d caracteres", domain.MaxSizeLength))
		}
		if utf8.RuneCountInString(it.Personalization) > domain.MaxPersonalizationInput {
			return domain.Invalid(fmt.Sprintf("items[%d].personalization", i),
				fmt.Sprintf("deve ter no máximo %d caracteres", domain.MaxPersonalizationInput))
		}
		if utf8.RuneCountInString(it.Color) > domain.MaxColorLength {
			return domain.Invalid(fmt.Sprintf("items[%d].color", i), fmt.Sprintf("deve ter no máximo %d caracteres", domain.MaxColorLength))
		}
		key := line{it.ProductID, strings.ToUpper(it.Size), strings.ToUpper(it.Color), domain.NormalizePersonalizationText(it.Personalization)}
		if _, dup := seen[key]; dup {
			return domain.Invalid(field, "produto repetido no mesmo tamanho, cor e personalização; some as quantidades em um item")
		}
		seen[key] = struct{}{}
	}
	return nil
}
