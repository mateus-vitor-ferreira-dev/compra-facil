package service

import (
	"context"
	"fmt"
	"time"

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
)

type ItemToValidate struct {
	ProductID uuid.UUID
	Quantity  int
	// ExpectedUnitPriceCents é o preço que o cliente viu; nil não confere.
	ExpectedUnitPriceCents *int64
}

type ValidatedItem struct {
	ProductID uuid.UUID
	Status    ItemStatus
	Quantity  int
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
	v := ValidatedItem{ProductID: it.ProductID, Quantity: it.Quantity}
	p, ok := byID[it.ProductID]
	if !ok {
		v.Status = ItemNotFound
		return v
	}
	v.Product = &p
	v.UnitPriceCents = p.EffectivePriceCents()

	switch {
	case p.Status != domain.ProductActive:
		v.Status = ItemNotActive
	case it.Quantity < MinItemQuantity || it.Quantity > MaxItemQuantity:
		v.Status = ItemInvalidQuantity
	case it.ExpectedUnitPriceCents != nil && *it.ExpectedUnitPriceCents != v.UnitPriceCents:
		v.Status = ItemPriceChanged
	default:
		v.Status = ItemOK
		v.LineTotalCents = v.UnitPriceCents * int64(it.Quantity)
	}
	return v
}

// validateItemsRequest recusa requisições malformadas (viram INVALID_ARGUMENT no gRPC).
func validateItemsRequest(items []ItemToValidate) error {
	if len(items) == 0 || len(items) > MaxOrderItems {
		return domain.Invalid("items", fmt.Sprintf("deve ter de 1 a %d itens", MaxOrderItems))
	}
	seen := make(map[uuid.UUID]struct{}, len(items))
	for i, it := range items {
		field := fmt.Sprintf("items[%d].product_id", i)
		if it.ProductID == uuid.Nil {
			return domain.Invalid(field, "é obrigatório")
		}
		if _, dup := seen[it.ProductID]; dup {
			return domain.Invalid(field, "produto repetido; some as quantidades em um item")
		}
		seen[it.ProductID] = struct{}{}
	}
	return nil
}
