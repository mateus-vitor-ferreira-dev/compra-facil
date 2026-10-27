package service

import (
	"errors"
	"slices"
	"testing"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/mock"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

func newProductService() (*ProductService, *productRepoMock, *categoryRepoMock) {
	p, c := &productRepoMock{}, &categoryRepoMock{}
	return NewProductService(p, c, clock), p, c
}

func validationFields(t *testing.T, err error) []string {
	t.Helper()
	var ve *domain.ValidationError
	require.ErrorAs(t, err, &ve)
	out := make([]string, len(ve.Fields))
	for i, f := range ve.Fields {
		out[i] = f.Field
	}
	return out
}

func ptr[T any](v T) *T { return &v }

func TestProductService_ListPublic_BuildsFilter(t *testing.T) {
	svc, products, categories := newProductService()
	cat := domain.Category{ID: uuid.New(), Slug: "tv"}
	subtree := []uuid.UUID{cat.ID, uuid.New()}
	categories.On("GetBySlug", anyArg, "tv").Return(cat, nil)
	categories.On("SubtreeIDs", anyArg, cat.ID).Return(subtree, nil)
	page := domain.Page[domain.Product]{Items: []domain.Product{{SKU: "TV-1"}}, NextCursor: "abc"}
	products.On("List", anyArg, mock.MatchedBy(func(f domain.ProductFilter) bool {
		return *f.Status == domain.ProductActive && // público só vê ativos
			f.Query == "qled" && // espaços removidos
			assert.ObjectsAreEqual(subtree, f.CategoryIDs) &&
			*f.MinPriceCents == 100 && f.OnSale && f.Now.Equal(fixedNow) && f.Limit == 10
	})).Return(page, nil)

	got, err := svc.ListPublic(ctx, ListProductsInput{
		Query: "  qled ", CategorySlug: "tv", MinPriceCents: ptr[int64](100), OnSale: true, Limit: 10,
	})

	require.NoError(t, err)
	assert.Equal(t, page, got)
	products.AssertExpectations(t)
}

func TestProductService_ListBrands_SameFiltersWithoutBrandAndPaging(t *testing.T) {
	svc, products, categories := newProductService()
	cat := domain.Category{ID: uuid.New(), Slug: "notebooks"}
	categories.On("GetBySlug", anyArg, "notebooks").Return(cat, nil)
	categories.On("SubtreeIDs", anyArg, cat.ID).Return([]uuid.UUID{cat.ID}, nil)
	want := []domain.BrandCount{{Name: "Dell", Count: 3}}
	products.On("Brands", anyArg, mock.MatchedBy(func(f domain.ProductFilter) bool {
		return *f.Status == domain.ProductActive && f.OnSale && len(f.Brands) == 0 && f.Sort == "" && f.Cursor == "" &&
			assert.ObjectsAreEqual([]uuid.UUID{cat.ID}, f.CategoryIDs)
	}), 0).Return(want, nil)

	got, err := svc.ListBrands(ctx, ListProductsInput{
		CategorySlug: "notebooks", OnSale: true, Brands: []string{"Dell"}, Sort: domain.SortPriceAsc, Cursor: "abc",
	})

	require.NoError(t, err)
	assert.Equal(t, want, got)
	products.AssertExpectations(t)
}

func TestProductService_ListPublic_UnknownCategoryIsEmpty(t *testing.T) {
	svc, products, categories := newProductService()
	categories.On("GetBySlug", anyArg, "nao-existe").Return(domain.Category{}, domain.ErrNotFound)

	got, err := svc.ListPublic(ctx, ListProductsInput{CategorySlug: "nao-existe"})

	require.NoError(t, err)
	assert.Empty(t, got.Items)
	assert.NotNil(t, got.Items, "serializa como [] e não null")
	products.AssertNotCalled(t, "List", anyArg, anyArg)
}

func TestProductService_ListPublic_Validation(t *testing.T) {
	tests := []struct {
		name string
		in   ListProductsInput
		want []string
	}{
		{"busca curta", ListProductsInput{Query: "a"}, []string{"q"}},
		{"limit acima do máximo", ListProductsInput{Limit: 101}, []string{"limit"}},
		{"preço negativo", ListProductsInput{MinPriceCents: ptr[int64](-1)}, []string{"min_price_cents"}},
		{"faixa invertida", ListProductsInput{MinPriceCents: ptr[int64](500), MaxPriceCents: ptr[int64](100)}, []string{"max_price_cents"}},
		{"vários", ListProductsInput{Query: "a", Limit: -1}, []string{"q", "limit"}},
		{"ordem que não existe", ListProductsInput{Sort: "barato"}, []string{"sort"}},
		{"página além do máximo", ListProductsInput{Page: MaxPage + 1}, []string{"page"}},
		{"página e cursor juntos", ListProductsInput{Page: 2, Cursor: "abc"}, []string{"cursor"}},
		{"marca vazia", ListProductsInput{Brands: []string{"Dell", " "}}, []string{"brand"}},
		{"marcas demais", ListProductsInput{Brands: slices.Repeat([]string{"Dell"}, 21)}, []string{"brand"}},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			svc, _, _ := newProductService()
			_, err := svc.ListPublic(ctx, tt.in)
			assert.Equal(t, tt.want, validationFields(t, err))
		})
	}
}

func TestProductService_ListPublic_Errors(t *testing.T) {
	t.Run("cursor inválido vira erro de validação", func(t *testing.T) {
		svc, products, _ := newProductService()
		products.On("List", anyArg, anyArg).Return(domain.Page[domain.Product]{}, domain.ErrInvalidCursor)

		_, err := svc.ListPublic(ctx, ListProductsInput{Cursor: "lixo"})
		assert.Equal(t, []string{"cursor"}, validationFields(t, err))
	})
	t.Run("erro de banco propaga", func(t *testing.T) {
		svc, products, _ := newProductService()
		boom := errors.New("conexão caiu")
		products.On("List", anyArg, anyArg).Return(domain.Page[domain.Product]{}, boom)

		_, err := svc.ListPublic(ctx, ListProductsInput{})
		assert.ErrorIs(t, err, boom)
	})
	t.Run("erro ao ler subárvore propaga", func(t *testing.T) {
		svc, _, categories := newProductService()
		cat := domain.Category{ID: uuid.New()}
		boom := errors.New("timeout")
		categories.On("GetBySlug", anyArg, "tv").Return(cat, nil)
		categories.On("SubtreeIDs", anyArg, cat.ID).Return([]uuid.UUID(nil), boom)

		_, err := svc.ListPublic(ctx, ListProductsInput{CategorySlug: "tv"})
		assert.ErrorIs(t, err, boom)
	})
}

func TestProductService_GetPublic(t *testing.T) {
	id := uuid.New()
	tests := []struct {
		name    string
		product domain.Product
		repoErr error
		wantErr error
	}{
		{"ativo", domain.Product{ID: id, Status: domain.ProductActive}, nil, nil},
		{"rascunho é 404", domain.Product{ID: id, Status: domain.ProductDraft}, nil, domain.ErrNotFound},
		{"inativo é 404", domain.Product{ID: id, Status: domain.ProductInactive}, nil, domain.ErrNotFound},
		{"inexistente", domain.Product{}, domain.ErrNotFound, domain.ErrNotFound},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			svc, products, _ := newProductService()
			products.On("GetByID", anyArg, id, fixedNow).Return(tt.product, tt.repoErr)

			got, err := svc.GetPublic(ctx, id)

			if tt.wantErr != nil {
				require.ErrorIs(t, err, tt.wantErr)
				return
			}
			require.NoError(t, err)
			assert.Equal(t, id, got.ID)
		})
	}
}

func TestProductService_GetMany(t *testing.T) {
	svc, products, _ := newProductService()
	ids := []uuid.UUID{uuid.New()}
	products.On("GetByIDs", anyArg, ids, fixedNow).Return([]domain.Product{{ID: ids[0]}}, nil)

	got, err := svc.GetMany(ctx, ids)
	require.NoError(t, err)
	assert.Len(t, got, 1)

	_, err = svc.GetMany(ctx, make([]uuid.UUID, 101))
	assert.Equal(t, []string{"product_ids"}, validationFields(t, err))
}

func TestProductService_AdminList(t *testing.T) {
	svc, products, _ := newProductService()
	draft := domain.ProductDraft
	products.On("List", anyArg, mock.MatchedBy(func(f domain.ProductFilter) bool {
		return *f.Status == draft && f.SKU == "TV-1"
	})).Return(domain.Page[domain.Product]{}, nil)

	_, err := svc.AdminList(ctx, AdminListInput{Status: &draft, SKU: " TV-1 "})
	require.NoError(t, err)

	bad := domain.ProductStatus("x")
	_, err = svc.AdminList(ctx, AdminListInput{Status: &bad})
	assert.Equal(t, []string{"status"}, validationFields(t, err))

	_, err = svc.AdminList(ctx, AdminListInput{Limit: 500})
	assert.Equal(t, []string{"limit"}, validationFields(t, err))

	_, err = svc.AdminList(ctx, AdminListInput{Query: "a"})
	assert.Equal(t, []string{"q"}, validationFields(t, err))
}

func TestProductService_AdminList_SearchesAnyStatus(t *testing.T) {
	svc, products, _ := newProductService()
	products.On("List", anyArg, mock.MatchedBy(func(f domain.ProductFilter) bool {
		return f.Status == nil && f.Query == "smart tv"
	})).Return(domain.Page[domain.Product]{}, nil)

	_, err := svc.AdminList(ctx, AdminListInput{Query: "  smart tv "})

	require.NoError(t, err)
	products.AssertExpectations(t)
}

func validInput(categoryID uuid.UUID) ProductInput {
	return ProductInput{
		SKU: " TV-55 ", Name: "Smart TV 55", CategoryID: categoryID, PriceCents: 199990, Status: domain.ProductActive,
	}
}

func TestProductService_Create(t *testing.T) {
	catID := uuid.New()

	t.Run("normaliza, grava e relê", func(t *testing.T) {
		svc, products, _ := newProductService()
		var created *domain.Product
		products.On("Create", anyArg, mock.AnythingOfType("*domain.Product")).
			Run(func(args mock.Arguments) { created = args.Get(1).(*domain.Product) }).Return(nil)
		products.On("GetByID", anyArg, mock.AnythingOfType("uuid.UUID"), fixedNow).
			Return(domain.Product{SKU: "TV-55", Category: domain.CategoryRef{Slug: "tv"}}, nil)

		got, err := svc.Create(ctx, validInput(catID))

		require.NoError(t, err)
		assert.Equal(t, "TV-55", created.SKU, "SKU sem espaços")
		assert.Equal(t, "BRL", created.Currency)
		assert.Equal(t, "tv", got.Category.Slug, "resposta vem da releitura")
	})
	t.Run("inválido não chega ao banco", func(t *testing.T) {
		svc, products, _ := newProductService()
		in := validInput(catID)
		in.PriceCents = 0

		_, err := svc.Create(ctx, in)

		assert.Equal(t, []string{"price_cents"}, validationFields(t, err))
		products.AssertNotCalled(t, "Create", anyArg, anyArg)
	})

	writeErrors := []struct {
		name    string
		repoErr error
		check   func(t *testing.T, err error)
	}{
		{"categoria inexistente vira 422", domain.ErrInvalidReference, func(t *testing.T, err error) {
			assert.Equal(t, []string{"category_id"}, validationFields(t, err))
		}},
		{"SKU duplicado vira conflito legível", domain.ErrConflict, func(t *testing.T, err error) {
			require.ErrorIs(t, err, domain.ErrConflict)
			assert.Contains(t, err.Error(), "SKU TV-55 já cadastrado")
		}},
		{"erro inesperado propaga", errors.New("disco cheio"), func(t *testing.T, err error) {
			assert.ErrorContains(t, err, "disco cheio")
		}},
	}
	for _, tt := range writeErrors {
		t.Run(tt.name, func(t *testing.T) {
			svc, products, _ := newProductService()
			products.On("Create", anyArg, anyArg).Return(tt.repoErr)
			_, err := svc.Create(ctx, validInput(catID))
			tt.check(t, err)
		})
	}
}

func TestProductService_Update(t *testing.T) {
	id, catID := uuid.New(), uuid.New()

	t.Run("passa a versão esperada e relê", func(t *testing.T) {
		svc, products, _ := newProductService()
		products.On("Update", anyArg, mock.MatchedBy(func(p *domain.Product) bool { return p.ID == id && p.SKU == "TV-55" }), 3).Return(nil)
		products.On("GetByID", anyArg, id, fixedNow).Return(domain.Product{ID: id, Version: 4}, nil)

		got, err := svc.Update(ctx, id, validInput(catID), 3)

		require.NoError(t, err)
		assert.Equal(t, 4, got.Version)
	})
	t.Run("versão desatualizada propaga", func(t *testing.T) {
		svc, products, _ := newProductService()
		products.On("Update", anyArg, anyArg, 1).Return(domain.ErrVersionConflict)

		_, err := svc.Update(ctx, id, validInput(catID), 1)
		assert.ErrorIs(t, err, domain.ErrVersionConflict)
	})
	t.Run("inválido não chega ao banco", func(t *testing.T) {
		svc, products, _ := newProductService()
		in := validInput(catID)
		in.SKU = "x"

		_, err := svc.Update(ctx, id, in, 1)
		assert.Equal(t, []string{"sku"}, validationFields(t, err))
		products.AssertNotCalled(t, "Update", anyArg, anyArg, anyArg)
	})
}

func TestProductService_AdminGetAndDelete(t *testing.T) {
	svc, products, _ := newProductService()
	id := uuid.New()
	products.On("GetByID", anyArg, id, fixedNow).Return(domain.Product{ID: id, Status: domain.ProductDraft}, nil)
	products.On("Delete", anyArg, id).Return(domain.ErrNotFound)

	got, err := svc.AdminGet(ctx, id)
	require.NoError(t, err)
	assert.Equal(t, domain.ProductDraft, got.Status, "admin vê rascunho")

	assert.ErrorIs(t, svc.Delete(ctx, id), domain.ErrNotFound)
}
