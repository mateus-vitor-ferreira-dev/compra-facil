package service

import (
	"errors"
	"strings"
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

func TestProductService_ValidateItems_Sizes(t *testing.T) {
	camiseta := domain.Product{ID: uuid.New(), SKU: "CAM-1", PriceCents: 7990, Currency: "BRL", Status: domain.ProductActive,
		Sizes: []string{"P", "M", "G"}}
	caneca := domain.Product{ID: uuid.New(), SKU: "CAN-1", PriceCents: 5990, Currency: "BRL", Status: domain.ProductActive}

	svc, products, _ := newProductService()
	products.On("GetByIDs", anyArg, anyArg, fixedNow).Return([]domain.Product{camiseta, caneca}, nil)

	t.Run("o mesmo produto em dois tamanhos, cada um no seu item", func(t *testing.T) {
		got, err := svc.ValidateItems(ctx, []ItemToValidate{
			{ProductID: camiseta.ID, Quantity: 1, Size: "p"},
			{ProductID: camiseta.ID, Quantity: 2, Size: "G"},
			{ProductID: caneca.ID, Quantity: 1},
		})
		require.NoError(t, err)
		assert.True(t, got.Valid)
		assert.Equal(t, "P", got.Items[0].Size, "volta como está no cadastro")
		assert.Equal(t, "G", got.Items[1].Size)
		assert.Empty(t, got.Items[2].Size)
		assert.Equal(t, int64(7990*3+5990), got.TotalCents)
	})

	t.Run("tamanho que o produto não tem, faltando ou sobrando", func(t *testing.T) {
		got, err := svc.ValidateItems(ctx, []ItemToValidate{
			{ProductID: camiseta.ID, Quantity: 1, Size: "GG"},
			{ProductID: camiseta.ID, Quantity: 1},
			{ProductID: caneca.ID, Quantity: 1, Size: "M"},
		})
		require.NoError(t, err)
		assert.False(t, got.Valid)
		for i, it := range got.Items {
			assert.Equal(t, ItemInvalidSize, it.Status, "item %d", i)
		}
		assert.Zero(t, got.TotalCents)
	})
}

func TestProductService_ValidateItems_Personalization(t *testing.T) {
	camiseta := domain.Product{ID: uuid.New(), SKU: "CAM-NOME", PriceCents: 7990, Currency: "BRL", Status: domain.ProductActive,
		Sizes:           []string{"P", "M"},
		Personalization: &domain.Personalization{Label: "Nome", MaxLength: 12, Required: true, Template: "nome-classico"}}
	caneca := domain.Product{ID: uuid.New(), SKU: "CAN-1", PriceCents: 5990, Currency: "BRL", Status: domain.ProductActive}

	svc, products, _ := newProductService()
	products.On("GetByIDs", anyArg, anyArg, fixedNow).Return([]domain.Product{camiseta, caneca}, nil)

	t.Run("o mesmo produto e tamanho com dois nomes, cada um no seu item", func(t *testing.T) {
		got, err := svc.ValidateItems(ctx, []ItemToValidate{
			{ProductID: camiseta.ID, Quantity: 1, Size: "M", Personalization: "  Ana   Maria "},
			{ProductID: camiseta.ID, Quantity: 1, Size: "M", Personalization: "João"},
			{ProductID: caneca.ID, Quantity: 1},
		})
		require.NoError(t, err)
		assert.True(t, got.Valid)
		assert.Equal(t, "Ana Maria", got.Items[0].Personalization, "volta normalizado")
		assert.Equal(t, "João", got.Items[1].Personalization)
		assert.Empty(t, got.Items[2].Personalization)
	})

	t.Run("faltando, longo, com emoji ou no produto que não tem", func(t *testing.T) {
		got, err := svc.ValidateItems(ctx, []ItemToValidate{
			{ProductID: camiseta.ID, Quantity: 1, Size: "M"},
			{ProductID: camiseta.ID, Quantity: 1, Size: "P", Personalization: "Maria Eduarda Souza"},
			{ProductID: camiseta.ID, Quantity: 2, Size: "P", Personalization: "Bia 🎄"},
			{ProductID: caneca.ID, Quantity: 1, Personalization: "Ana"},
		})
		require.NoError(t, err)
		assert.False(t, got.Valid)
		for i, it := range got.Items {
			assert.Equal(t, ItemInvalidPersonalization, it.Status, "item %d", i)
		}
	})
}

func TestProductService_ValidateItems_Colors(t *testing.T) {
	camiseta := domain.Product{ID: uuid.New(), SKU: "CAM-COR", PriceCents: 7990, Currency: "BRL", Status: domain.ProductActive,
		Sizes: []string{"M"}, Colors: []string{"Branco", "Preto"}}
	caneca := domain.Product{ID: uuid.New(), SKU: "CAN-1", PriceCents: 5990, Currency: "BRL", Status: domain.ProductActive}

	svc, products, _ := newProductService()
	products.On("GetByIDs", anyArg, anyArg, fixedNow).Return([]domain.Product{camiseta, caneca}, nil)

	t.Run("o mesmo produto e tamanho em duas cores, cada uma no seu item", func(t *testing.T) {
		got, err := svc.ValidateItems(ctx, []ItemToValidate{
			{ProductID: camiseta.ID, Quantity: 1, Size: "M", Color: "preto"},
			{ProductID: camiseta.ID, Quantity: 1, Size: "M", Color: "Branco"},
			{ProductID: caneca.ID, Quantity: 1},
		})
		require.NoError(t, err)
		assert.True(t, got.Valid)
		assert.Equal(t, "Preto", got.Items[0].Color, "volta como está no cadastro")
		assert.Equal(t, "Branco", got.Items[1].Color)
		assert.Empty(t, got.Items[2].Color)
	})

	t.Run("cor que o produto não tem, faltando ou sobrando", func(t *testing.T) {
		got, err := svc.ValidateItems(ctx, []ItemToValidate{
			{ProductID: camiseta.ID, Quantity: 1, Size: "M", Color: "Vinho"},
			{ProductID: camiseta.ID, Quantity: 1, Size: "M"},
			{ProductID: caneca.ID, Quantity: 1, Color: "Preto"},
		})
		require.NoError(t, err)
		assert.False(t, got.Valid)
		for i, it := range got.Items {
			assert.Equal(t, ItemInvalidColor, it.Status, "item %d", i)
		}
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
		{"repetido no mesmo tamanho", []ItemToValidate{{ProductID: a, Quantity: 1, Size: "G"}, {ProductID: a, Quantity: 2, Size: "g"}}, "items[1].product_id"},
		{"mesmo texto depois de normalizar", []ItemToValidate{{ProductID: a, Quantity: 1, Personalization: "Ana"}, {ProductID: a, Quantity: 1, Personalization: " Ana "}}, "items[1].product_id"},
		{"mesma cor sem diferenciar maiúsculas", []ItemToValidate{{ProductID: a, Quantity: 1, Color: "Preto"}, {ProductID: a, Quantity: 1, Color: "PRETO"}}, "items[1].product_id"},
		{"cor longa", []ItemToValidate{{ProductID: a, Quantity: 1, Color: strings.Repeat("x", domain.MaxColorLength+1)}}, "items[0].color"},
		{"texto enorme", []ItemToValidate{{ProductID: a, Quantity: 1, Personalization: strings.Repeat("a", domain.MaxPersonalizationInput+1)}}, "items[0].personalization"},
		{"tamanho longo", []ItemToValidate{{ProductID: a, Quantity: 1, Size: "XXXXXXXXXXL"}}, "items[0].size"},
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
