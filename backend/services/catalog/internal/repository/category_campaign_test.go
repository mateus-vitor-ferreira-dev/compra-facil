package repository

import (
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

func TestCategoryRepository(t *testing.T) {
	resetDB(t)
	repo := NewCategoryRepository(testDB)
	eletro := mustCategory(t, "eletronicos", nil)
	tv := mustCategory(t, "tv", &eletro.ID)
	oled := mustCategory(t, "oled", &tv.ID)
	mustCategory(t, "moveis", nil)

	t.Run("slug duplicado", func(t *testing.T) {
		dup := domain.Category{Slug: "tv", Name: "Outra"}
		assert.ErrorIs(t, repo.Create(ctx, &dup), domain.ErrConflict)
	})
	t.Run("busca por slug", func(t *testing.T) {
		got, err := repo.GetBySlug(ctx, "tv")
		require.NoError(t, err)
		assert.Equal(t, &eletro.ID, got.ParentID)

		_, err = repo.GetBySlug(ctx, "nao-existe")
		assert.ErrorIs(t, err, domain.ErrNotFound)
	})
	t.Run("subárvore em vários níveis", func(t *testing.T) {
		ids, err := repo.SubtreeIDs(ctx, eletro.ID)
		require.NoError(t, err)
		assert.ElementsMatch(t, []uuid.UUID{eletro.ID, tv.ID, oled.ID}, ids)

		_, err = repo.SubtreeIDs(ctx, uuid.New())
		assert.ErrorIs(t, err, domain.ErrNotFound)
	})
	t.Run("lista por nome", func(t *testing.T) {
		all, err := repo.List(ctx)
		require.NoError(t, err)
		require.Len(t, all, 4)
		assert.Equal(t, "Cat eletronicos", all[0].Name)
	})
}

func TestCampaignRepository_CRUD(t *testing.T) {
	resetDB(t)
	repo := NewCampaignRepository(testDB)
	c := mustCampaign(t, "bf", 30, now, now.Add(24*time.Hour))

	c.Name, c.DiscountPercent = "Black Friday", 40
	require.NoError(t, repo.Update(ctx, &c))

	got, err := repo.GetByID(ctx, c.ID)
	require.NoError(t, err)
	assert.Equal(t, "Black Friday", got.Name)
	assert.Equal(t, 40, got.DiscountPercent)
	assert.True(t, got.StartsAt.Equal(now))

	dup := domain.Campaign{Slug: "bf", Name: "x", DiscountPercent: 10, StartsAt: now, EndsAt: now.Add(time.Hour)}
	require.ErrorIs(t, repo.Create(ctx, &dup), domain.ErrConflict)

	invalid := domain.Campaign{Slug: "inv", Name: "x", DiscountPercent: 10, StartsAt: now, EndsAt: now.Add(-time.Hour)}
	require.ErrorIs(t, repo.Create(ctx, &invalid), domain.ErrInvalidReference, "CHECK de período barra no banco")

	missing := domain.Campaign{ID: uuid.New(), Slug: "m", Name: "m", DiscountPercent: 1, StartsAt: now, EndsAt: now.Add(time.Hour)}
	require.ErrorIs(t, repo.Update(ctx, &missing), domain.ErrNotFound)

	require.NoError(t, repo.Delete(ctx, c.ID))
	_, err = repo.GetByID(ctx, c.ID)
	require.ErrorIs(t, err, domain.ErrNotFound)
	assert.ErrorIs(t, repo.Delete(ctx, c.ID), domain.ErrNotFound)
}

func TestCampaignRepository_SetProducts(t *testing.T) {
	resetDB(t)
	repo := NewCampaignRepository(testDB)
	cat := mustCategory(t, "tv", nil)
	a, b, c := mustProduct(t, "A-01", cat.ID), mustProduct(t, "B-01", cat.ID), mustProduct(t, "C-01", cat.ID)
	camp := mustCampaign(t, "bf", 30, now, now.Add(time.Hour), a.ID, b.ID)

	got, err := repo.GetByID(ctx, camp.ID)
	require.NoError(t, err)
	assert.Equal(t, 2, got.ProductCount)

	require.NoError(t, repo.SetProducts(ctx, camp.ID, []uuid.UUID{c.ID}), "substitui a lista")
	got, err = repo.GetByID(ctx, camp.ID)
	require.NoError(t, err)
	assert.Equal(t, 1, got.ProductCount)

	require.ErrorIs(t, repo.SetProducts(ctx, uuid.New(), []uuid.UUID{a.ID}), domain.ErrNotFound, "campanha inexistente")
	require.ErrorIs(t, repo.SetProducts(ctx, camp.ID, []uuid.UUID{uuid.New()}), domain.ErrInvalidReference, "produto inexistente")

	got, err = repo.GetByID(ctx, camp.ID)
	require.NoError(t, err)
	assert.Equal(t, 1, got.ProductCount, "falha não apaga a lista anterior (rollback)")

	require.NoError(t, repo.Delete(ctx, camp.ID), "remove em cascata os vínculos")
}

func TestCampaignRepository_ListActiveAndPaginate(t *testing.T) {
	resetDB(t)
	repo := NewCampaignRepository(testDB)
	mustCampaign(t, "passada", 10, now.Add(-48*time.Hour), now.Add(-24*time.Hour))
	mustCampaign(t, "vigente", 20, now.Add(-time.Hour), now.Add(time.Hour))
	mustCampaign(t, "acaba-agora", 20, now.Add(-time.Hour), now) // fim exclusivo
	mustCampaign(t, "futura", 30, now.Add(time.Hour), now.Add(48*time.Hour))

	active, err := repo.ListActive(ctx, now)
	require.NoError(t, err)
	require.Len(t, active, 1)
	assert.Equal(t, "vigente", active[0].Slug)

	first, err := repo.List(ctx, "", 3)
	require.NoError(t, err)
	require.Len(t, first.Items, 3)
	assert.Equal(t, "futura", first.Items[0].Slug, "começa mais tarde primeiro")
	require.NotEmpty(t, first.NextCursor)

	second, err := repo.List(ctx, first.NextCursor, 3)
	require.NoError(t, err)
	require.Len(t, second.Items, 1)
	assert.Equal(t, "passada", second.Items[0].Slug)
	assert.Empty(t, second.NextCursor)
}

func TestCampaignRepository_NextStart(t *testing.T) {
	resetDB(t)
	repo := NewCampaignRepository(testDB)

	next, err := repo.NextStart(ctx, now)
	require.NoError(t, err)
	assert.True(t, next.IsZero(), "sem campanha agendada")

	mustCampaign(t, "vigente", 20, now.Add(-time.Hour), now.Add(time.Hour))
	mustCampaign(t, "comeca-agora", 10, now, now.Add(time.Hour)) // já começou: não é a próxima
	mustCampaign(t, "depois", 30, now.Add(3*time.Hour), now.Add(48*time.Hour))
	mustCampaign(t, "proxima", 15, now.Add(2*time.Hour), now.Add(4*time.Hour))

	next, err = repo.NextStart(ctx, now)
	require.NoError(t, err)
	assert.True(t, now.Add(2*time.Hour).Equal(next), "esperava o início de 'proxima', veio %s", next)
}

func TestCampaignRepository_OverlappingProducts(t *testing.T) {
	resetDB(t)
	repo := NewCampaignRepository(testDB)
	cat := mustCategory(t, "tv", nil)
	a, b, c := mustProduct(t, "A-01", cat.ID), mustProduct(t, "B-01", cat.ID), mustProduct(t, "C-01", cat.ID)
	// "bf" vai de now até now+24h com A e B
	bf := mustCampaign(t, "bf", 30, now, now.Add(24*time.Hour), a.ID, b.ID)
	nova := mustCampaign(t, "nova", 10, now.Add(48*time.Hour), now.Add(72*time.Hour), c.ID)

	tests := []struct {
		name       string
		start, end time.Time
		ids        []uuid.UUID
		want       []uuid.UUID
	}{
		{"período cruza: A conflita", now.Add(12 * time.Hour), now.Add(36 * time.Hour), []uuid.UUID{a.ID, c.ID}, []uuid.UUID{a.ID}},
		{"começa quando a outra termina: sem conflito", now.Add(24 * time.Hour), now.Add(48 * time.Hour), []uuid.UUID{a.ID}, nil},
		{"lista vazia", now, now.Add(time.Hour), []uuid.UUID{}, nil},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got, err := repo.OverlappingProducts(ctx, nova.ID, tt.start, tt.end, tt.ids)
			require.NoError(t, err)
			assert.ElementsMatch(t, tt.want, got)
		})
	}

	t.Run("nil usa os produtos atuais da campanha (mudança de datas)", func(t *testing.T) {
		require.NoError(t, repo.SetProducts(ctx, nova.ID, []uuid.UUID{b.ID, c.ID}))
		got, err := repo.OverlappingProducts(ctx, nova.ID, now, now.Add(time.Hour), nil)
		require.NoError(t, err)
		assert.Equal(t, []uuid.UUID{b.ID}, got)
	})
	t.Run("ignora a própria campanha", func(t *testing.T) {
		got, err := repo.OverlappingProducts(ctx, bf.ID, now, now.Add(time.Hour), []uuid.UUID{a.ID})
		require.NoError(t, err)
		assert.Empty(t, got)
	})
}
