package repository

import (
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

// listAll percorre as páginas até o fim, seguindo o cursor.
func listAll(t *testing.T, repo *ProductRepository, f domain.ProductFilter) []string {
	t.Helper()
	var seen []string
	for {
		page, err := repo.List(ctx, f)
		require.NoError(t, err)
		seen = append(seen, skus(page.Items)...)
		if page.NextCursor == "" {
			return seen
		}
		f.Cursor = page.NextCursor
	}
}

func TestProductRepository_SortByEffectivePrice_AcrossPages(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	cat := mustCategory(t, "tv", nil)
	mustProduct(t, "A-300", cat.ID, withPrice(300))
	promo := mustProduct(t, "B-1000", cat.ID, withPrice(1000)) // com 90% sai por 100: o mais barato
	mustProduct(t, "C-200", cat.ID, withPrice(200))
	mustProduct(t, "D-200", cat.ID, withPrice(200)) // empate de preço: o id desempata
	mustProduct(t, "E-500", cat.ID, withPrice(500))
	mustCampaign(t, "bf", 90, now.Add(-time.Hour), now.Add(time.Hour), promo.ID)

	asc := listAll(t, repo, domain.ProductFilter{Now: now, Sort: domain.SortPriceAsc, Limit: 2})
	desc := listAll(t, repo, domain.ProductFilter{Now: now, Sort: domain.SortPriceDesc, Limit: 2})

	require.Len(t, asc, 5, "sem repetir nem pular entre páginas")
	assert.Equal(t, "B-1000", asc[0], "o preço é o efetivo, com o desconto")
	assert.ElementsMatch(t, []string{"C-200", "D-200"}, asc[1:3])
	assert.Equal(t, []string{"A-300", "E-500"}, asc[3:])
	require.Len(t, desc, 5)
	assert.Equal(t, "E-500", desc[0])
	assert.Equal(t, "B-1000", desc[4])
}

func TestProductRepository_SortByDiscount(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	cat := mustCategory(t, "tv", nil)
	small := mustProduct(t, "DESC-10", cat.ID)
	big := mustProduct(t, "DESC-50", cat.ID)
	mustProduct(t, "SEM-DESC", cat.ID)
	mustCampaign(t, "pouco", 10, now.Add(-time.Hour), now.Add(time.Hour), small.ID)
	mustCampaign(t, "muito", 50, now.Add(-time.Hour), now.Add(time.Hour), big.ID)

	got := listAll(t, repo, domain.ProductFilter{Now: now, Sort: domain.SortDiscount, Limit: 1})

	assert.Equal(t, []string{"DESC-50", "DESC-10", "SEM-DESC"}, got)
}

func TestProductRepository_CursorOnlyForItsOwnSort(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	cat := mustCategory(t, "tv", nil)
	for i := range 3 {
		mustProduct(t, seqSKU(i), cat.ID, withPrice(int64(100+i)))
	}
	page, err := repo.List(ctx, domain.ProductFilter{Now: now, Sort: domain.SortPriceAsc, Limit: 1})
	require.NoError(t, err)
	require.NotEmpty(t, page.NextCursor)

	_, err = repo.List(ctx, domain.ProductFilter{Now: now, Sort: domain.SortPriceDesc, Limit: 1, Cursor: page.NextCursor})
	require.ErrorIs(t, err, domain.ErrInvalidCursor)

	newest, err := repo.List(ctx, domain.ProductFilter{Now: now, Limit: 1})
	require.NoError(t, err)
	_, err = repo.List(ctx, domain.ProductFilter{Now: now, Sort: domain.SortNewest, Limit: 1, Cursor: newest.NextCursor})
	assert.NoError(t, err, "sem sort é o mesmo que newest")
}

func TestProductRepository_BrandFilterAndFacet(t *testing.T) {
	resetDB(t)
	repo := NewProductRepository(testDB)
	notebooks := mustCategory(t, "notebooks", nil)
	tv := mustCategory(t, "tv", nil)
	dellBarato := mustProduct(t, "NOTE-DELL-1", notebooks.ID, withBrand("Dell"), withPrice(300000))
	mustProduct(t, "NOTE-DELL-2", notebooks.ID, withBrand("Dell"), withPrice(500000))
	mustProduct(t, "NOTE-ACER", notebooks.ID, withBrand("Acer"), withPrice(200000))
	mustProduct(t, "NOTE-SEM", notebooks.ID)
	mustProduct(t, "TV-LG", tv.ID, withBrand("LG"))
	mustCampaign(t, "bf", 20, now.Add(-time.Hour), now.Add(time.Hour), dellBarato.ID)

	t.Run("filtro sem diferenciar maiúsculas, várias marcas", func(t *testing.T) {
		got := listAll(t, repo, domain.ProductFilter{Now: now, Brands: []string{"dell", " ACER "}, Sort: domain.SortPriceAsc})
		assert.Equal(t, []string{"NOTE-ACER", "NOTE-DELL-1", "NOTE-DELL-2"}, got)
	})
	t.Run("o notebook mais barato da Dell em promoção", func(t *testing.T) {
		got := listAll(t, repo, domain.ProductFilter{
			Now: now, CategoryIDs: []uuid.UUID{notebooks.ID}, Brands: []string{"Dell"}, OnSale: true, Sort: domain.SortPriceAsc,
		})
		assert.Equal(t, []string{"NOTE-DELL-1"}, got)
	})
	t.Run("faceta conta pelos outros filtros, sem o de marca", func(t *testing.T) {
		brands, err := repo.Brands(ctx, domain.ProductFilter{Now: now, CategoryIDs: []uuid.UUID{notebooks.ID}, Brands: []string{"Acer"}}, 0)
		require.NoError(t, err)
		assert.Equal(t, []domain.BrandCount{{Name: "Dell", Count: 2}, {Name: "Acer", Count: 1}}, brands)
	})
	t.Run("busca acha pela marca", func(t *testing.T) {
		got := listAll(t, repo, domain.ProductFilter{Now: now, Query: "acer"})
		assert.Equal(t, []string{"NOTE-ACER"}, got)
	})
}
