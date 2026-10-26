package service

import (
	"errors"
	"testing"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

func TestProductService_ValidateItems(t *testing.T) {
	tv := domain.Product{ID: uuid.New(), SKU: "TV-1", PriceCents: 10000, Currency: "BRL", Status: domain.ProductActive,
		ActiveCampaign: &domain.CampaignRef{DiscountPercent: 30}} // efetivo 7000
	fone := domain.Product{ID: uuid.New(), SKU: "FONE", PriceCents: 2990, Currency: "BRL", Status: domain.ProductActive}
	rascunho := domain.Product{ID: uuid.New(), SKU: "RASC", PriceCents: 100, Currency: "BRL", Status: domain.ProductDraft}
	missing := uuid.New()

	svc, products, _ := newProductService()
	products.On("GetByIDs", anyArg, anyArg, fixedNow).Return([]domain.Product{tv, fone, rascunho}, nil)

	t.Run("todos ok: preço efetivo e total", func(t *testing.T) {
		got, err := svc.ValidateItems(ctx, []ItemToValidate{
			{ProductID: tv.ID, Quantity: 2, ExpectedUnitPriceCents: ptr[int64](7000)},
			{ProductID: fone.ID, Quantity: 1},
		})
		require.NoError(t, err)
		assert.True(t, got.Valid)
		assert.Equal(t, int64(7000), got.Items[0].UnitPriceCents, "com desconto da campanha")
		assert.Equal(t, int64(14000), got.Items[0].LineTotalCents)
		assert.Equal(t, int64(14000+2990), got.TotalCents)
		assert.Equal(t, "BRL", got.Currency)
		assert.Equal(t, fixedNow, got.PricedAt)
	})

	t.Run("cada problema no seu item, na ordem da entrada", func(t *testing.T) {
		got, err := svc.ValidateItems(ctx, []ItemToValidate{
			{ProductID: missing, Quantity: 1},
			{ProductID: rascunho.ID, Quantity: 1},
			{ProductID: tv.ID, Quantity: 100},
			{ProductID: fone.ID, Quantity: 1, ExpectedUnitPriceCents: ptr[int64](2500)},
		})
		require.NoError(t, err)
		assert.False(t, got.Valid)
		statuses := []ItemStatus{got.Items[0].Status, got.Items[1].Status, got.Items[2].Status, got.Items[3].Status}
		assert.Equal(t, []ItemStatus{ItemNotFound, ItemNotActive, ItemInvalidQuantity, ItemPriceChanged}, statuses)
		assert.Nil(t, got.Items[0].Product, "inexistente não tem produto")
		assert.Equal(t, "RASC", got.Items[1].Product.SKU, "com problema, mas existente, traz o produto")
		assert.Equal(t, int64(2990), got.Items[3].UnitPriceCents, "devolve o preço atual para o orders explicar")
		assert.Zero(t, got.TotalCents, "itens com problema não somam")
	})
}

func TestProductService_ValidateItems_BadRequest(t *testing.T) {
	a := uuid.New()
	tests := []struct {
		name  string
		items []ItemToValidate
		field string
	}{
		{"vazio", nil, "items"},
		{"acima de 50", make([]ItemToValidate, MaxOrderItems+1), "items"},
		{"sem id", []ItemToValidate{{Quantity: 1}}, "items[0].product_id"},
		{"repetido", []ItemToValidate{{ProductID: a, Quantity: 1}, {ProductID: a, Quantity: 2}}, "items[1].product_id"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			svc, products, _ := newProductService()
			_, err := svc.ValidateItems(ctx, tt.items)
			assert.Equal(t, []string{tt.field}, validationFields(t, err))
			products.AssertNotCalled(t, "GetByIDs", anyArg, anyArg, anyArg)
		})
	}

	t.Run("erro do banco propaga", func(t *testing.T) {
		svc, products, _ := newProductService()
		boom := errors.New("timeout")
		products.On("GetByIDs", anyArg, anyArg, anyArg).Return([]domain.Product(nil), boom)
		_, err := svc.ValidateItems(ctx, []ItemToValidate{{ProductID: a, Quantity: 1}})
		assert.ErrorIs(t, err, boom)
	})
}
