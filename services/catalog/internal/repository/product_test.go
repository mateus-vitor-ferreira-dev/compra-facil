package repository

import (
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
)

func TestProductRepository_CreateAndGet(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	cat := mustCategory(t, "tv", nil)

	p := mustProduct(t, "TV-55", cat.ID, withPrice(19990))

	assert.NotEqual(t, uuid.Nil, p.ID)
	assert.Equal(t, 1, p.Version)
	assert.Equal(t, "BRL", p.Currency)

	got, err := repo.GetByID(ctx, p.ID, now)
	require.NoError(t, err)
	assert.Equal(t, "TV-55", got.SKU)
	assert.Equal(t, domain.CategoryRef{ID: cat.ID, Slug: "tv", Name: "Cat tv"}, got.Category)
	assert.Nil(t, got.ActiveCampaign)
	assert.Equal(t, int64(19990), got.EffectivePriceCents())

	_, err = repo.GetByID(ctx, uuid.New(), now)
	assert.ErrorIs(t, err, domain.ErrNotFound)
}

func TestProductRepository_CreateConstraints(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	cat := mustCategory(t, "tv", nil)
	first := mustProduct(t, "TV-55", cat.ID)

	dup := domain.Product{SKU: "TV-55", Name: "x", CategoryID: cat.ID, PriceCents: 1, Status: domain.ProductActive}
	require.ErrorIs(t, repo.Create(ctx, &dup), domain.ErrConflict, "SKU duplicado")

	noCat := domain.Product{SKU: "TV-99", Name: "x", CategoryID: uuid.New(), PriceCents: 1, Status: domain.ProductActive}
	require.ErrorIs(t, repo.Create(ctx, &noCat), domain.ErrInvalidReference, "categoria inexistente")

	require.NoError(t, repo.Delete(ctx, first.ID))
	again := domain.Product{SKU: "TV-55", Name: "x", CategoryID: cat.ID, PriceCents: 1, Status: domain.ProductActive}
	require.NoError(t, repo.Create(ctx, &again), "SKU de produto removido pode ser reaproveitado")
}

func TestProductRepository_UpdateOptimisticLock(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	cat := mustCategory(t, "tv", nil)
	p := mustProduct(t, "TV-55", cat.ID)

	p.Name, p.PriceCents = "Novo nome", 15000
	require.NoError(t, repo.Update(ctx, &p, 1))
	assert.Equal(t, 2, p.Version)

	got, err := repo.GetByID(ctx, p.ID, now)
	require.NoError(t, err)
	assert.Equal(t, "Novo nome", got.Name)
	assert.Equal(t, int64(15000), got.PriceCents)
	assert.Equal(t, 2, got.Version)

	stale := got
	require.ErrorIs(t, repo.Update(ctx, &stale, 1), domain.ErrVersionConflict)

	missing := domain.Product{ID: uuid.New(), SKU: "X-1", Name: "x", CategoryID: cat.ID, PriceCents: 1, Status: domain.ProductActive}
	assert.ErrorIs(t, repo.Update(ctx, &missing, 1), domain.ErrNotFound)
}

func TestProductRepository_Delete(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	p := mustProduct(t, "TV-55", mustCategory(t, "tv", nil).ID)

	require.NoError(t, repo.Delete(ctx, p.ID))

	_, err := repo.GetByID(ctx, p.ID, now)
	require.ErrorIs(t, err, domain.ErrNotFound)
	assert.ErrorIs(t, repo.Delete(ctx, p.ID), domain.ErrNotFound, "remover duas vezes")
}

func TestProductRepository_ActiveCampaignAndEffectivePrice(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	cat := mustCategory(t, "tv", nil)
	p := mustProduct(t, "TV-55", cat.ID, withPrice(999))

	mustCampaign(t, "fraca", 10, now.Add(-time.Hour), now.Add(time.Hour), p.ID)
	forte := mustCampaign(t, "forte", 15, now.Add(-time.Hour), now.Add(2*time.Hour), p.ID)
	mustCampaign(t, "futura", 50, now.Add(time.Hour), now.Add(3*time.Hour), p.ID)

	got, err := repo.GetByID(ctx, p.ID, now)
	require.NoError(t, err)
	require.NotNil(t, got.ActiveCampaign)
	assert.Equal(t, forte.ID, got.ActiveCampaign.ID, "vale a vigente de maior desconto")
	assert.Equal(t, int64(849), got.EffectivePriceCents(), "999 - 15%, arredondado para baixo")

	later, err := repo.GetByID(ctx, p.ID, now.Add(90*time.Minute))
	require.NoError(t, err)
	assert.Equal(t, 50, later.ActiveCampaign.DiscountPercent, "a futura passa a valer")
}

func TestProductRepository_GetByIDs(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	cat := mustCategory(t, "tv", nil)
	a, b := mustProduct(t, "A-01", cat.ID), mustProduct(t, "B-01", cat.ID)

	got, err := repo.GetByIDs(ctx, []uuid.UUID{a.ID, b.ID, uuid.New()}, now)
	require.NoError(t, err)
	assert.ElementsMatch(t, []string{"A-01", "B-01"}, skus(got), "ids desconhecidos são ignorados")

	empty, err := repo.GetByIDs(ctx, nil, now)
	require.NoError(t, err)
	assert.Empty(t, empty)
}

func TestProductRepository_ListKeysetPagination(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	cat := mustCategory(t, "tv", nil)
	for i := range 25 {
		mustProduct(t, seqSKU(i), cat.ID)
	}

	var seen []string
	cursor, pages := "", 0
	for {
		page, err := repo.List(ctx, domain.ProductFilter{Now: now, Cursor: cursor, Limit: 10})
		require.NoError(t, err)
		seen = append(seen, skus(page.Items)...)
		pages++
		if page.NextCursor == "" {
			break
		}
		cursor = page.NextCursor
	}

	assert.Equal(t, 3, pages)
	require.Len(t, seen, 25, "sem repetir nem pular itens")
	assert.Equal(t, seqSKU(24), seen[0], "mais novo primeiro")
	assert.Equal(t, seqSKU(0), seen[24])
}

func TestProductRepository_ListFilters(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	eletro := mustCategory(t, "eletronicos", nil)
	tv := mustCategory(t, "tv", &eletro.ID)
	moveis := mustCategory(t, "moveis", nil)

	tvCara := mustProduct(t, "TV-CARA", tv.ID, withPrice(500000))
	mustProduct(t, "TV-BARATA", tv.ID, withPrice(100000))
	mustProduct(t, "FONE", eletro.ID, withPrice(20000))
	mustProduct(t, "SOFA", moveis.ID, withPrice(300000))
	mustProduct(t, "RASCUNHO", tv.ID, withStatus(domain.ProductDraft))
	// TV-CARA com 80% sai por 100000
	mustCampaign(t, "bf", 80, now.Add(-time.Hour), now.Add(time.Hour), tvCara.ID)

	subtree, err := NewCategoryRepository(testDB).SubtreeIDs(ctx, eletro.ID)
	require.NoError(t, err)

	active := domain.ProductActive
	price := func(v int64) *int64 { return &v }
	tests := []struct {
		name   string
		filter domain.ProductFilter
		want   []string
	}{
		{"só ativos", domain.ProductFilter{Status: &active}, []string{"TV-CARA", "TV-BARATA", "FONE", "SOFA"}},
		{"categoria inclui subcategorias", domain.ProductFilter{Status: &active, CategoryIDs: subtree}, []string{"TV-CARA", "TV-BARATA", "FONE"}},
		{"faixa pelo preço efetivo", domain.ProductFilter{Status: &active, MinPriceCents: price(100000), MaxPriceCents: price(100000)}, []string{"TV-CARA", "TV-BARATA"}},
		{"em promoção", domain.ProductFilter{OnSale: true}, []string{"TV-CARA"}},
		{"sku exato", domain.ProductFilter{SKU: "SOFA"}, []string{"SOFA"}},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			tt.filter.Now = now
			page, err := repo.List(ctx, tt.filter)
			require.NoError(t, err)
			assert.ElementsMatch(t, tt.want, skus(page.Items))
			assert.Empty(t, page.NextCursor)
		})
	}
}

func TestProductRepository_Search(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	cat := mustCategory(t, "tv", nil)
	mustProduct(t, "TV-QLED-55", cat.ID, withName("Smart TV QLED 55 polegadas"))
	mustProduct(t, "TV-LED-32", cat.ID, withName("Televisor LED 32"))
	mustProduct(t, "GELADEIRA", cat.ID, withName("Geladeira Frost Free"))

	t.Run("tolera erro de digitação", func(t *testing.T) {
		page, err := repo.List(ctx, domain.ProductFilter{Now: now, Query: "televsor"})
		require.NoError(t, err)
		assert.Equal(t, []string{"TV-LED-32"}, skus(page.Items))
	})
	t.Run("acha por sku e ignora caixa", func(t *testing.T) {
		page, err := repo.List(ctx, domain.ProductFilter{Now: now, Query: "Qled"})
		require.NoError(t, err)
		assert.Equal(t, []string{"TV-QLED-55"}, skus(page.Items))
	})
	t.Run("pagina por offset", func(t *testing.T) {
		for i := range 5 {
			mustProduct(t, seqSKU(i), cat.ID, withName("Fogão 4 bocas"))
		}
		first, err := repo.List(ctx, domain.ProductFilter{Now: now, Query: "fogao", Limit: 3})
		require.NoError(t, err)
		require.Len(t, first.Items, 3)
		require.NotEmpty(t, first.NextCursor)

		second, err := repo.List(ctx, domain.ProductFilter{Now: now, Query: "fogao", Limit: 3, Cursor: first.NextCursor})
		require.NoError(t, err)
		assert.Len(t, second.Items, 2)
		assert.Empty(t, second.NextCursor)
		assert.NotContains(t, skus(second.Items), first.Items[0].SKU)
	})
}

func TestProductRepository_InvalidCursor(t *testing.T) {
	_, err := NewProductRepository(testDB).List(ctx, domain.ProductFilter{Now: now, Cursor: "%%%"})
	assert.ErrorIs(t, err, domain.ErrInvalidCursor)
}

func TestProductRepository_ImageVariants_KeptForTheSameURL_DroppedForANewOne(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	cat := mustCategory(t, "tv", nil)
	url := "http://localhost/media/produtos/tv-1.d226c7dd-683.webp"
	image := &domain.ProductImage{Width: 683, Height: 683, Variants: []domain.ImageVariant{
		{Width: 320, URL: "http://localhost/media/produtos/tv-1.d226c7dd-320.webp"}, {Width: 683, URL: url},
	}}
	p := mustProduct(t, "TV-55", cat.ID, func(p *domain.Product) { p.ImageURL, p.Image = &url, image })

	got, err := repo.GetByID(ctx, p.ID, now)
	require.NoError(t, err)
	assert.Equal(t, image, got.Image)

	// o admin muda outro campo e reenvia a mesma URL (o PUT não traz as variantes): elas ficam
	got.Name, got.Image = "Outro nome", nil
	require.NoError(t, repo.Update(ctx, &got, got.Version))
	kept, err := repo.GetByID(ctx, p.ID, now)
	require.NoError(t, err)
	assert.Equal(t, image, kept.Image)

	// URL nova: as variantes eram da foto anterior
	other := "https://cdn.example.com/outra.jpg"
	kept.ImageURL = &other
	require.NoError(t, repo.Update(ctx, &kept, kept.Version))
	changed, err := repo.GetByID(ctx, p.ID, now)
	require.NoError(t, err)
	assert.Equal(t, other, *changed.ImageURL)
	assert.Nil(t, changed.Image)
}
