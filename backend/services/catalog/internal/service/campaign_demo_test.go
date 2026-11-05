package service

import (
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/mock"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

func TestCampaignService_KeepOneActive(t *testing.T) {
	day := 24 * time.Hour
	camp := func(slug string, start time.Time, days int) domain.Campaign {
		return domain.Campaign{ID: uuid.New(), Slug: slug, Name: slug, DiscountPercent: 20, StartsAt: start, EndsAt: start.Add(time.Duration(days) * day)}
	}

	t.Run("com uma ativa, não mexe em nada", func(t *testing.T) {
		repo := &campaignRepoMock{}
		repo.On("ListActive", anyArg, anyArg).Return([]domain.Campaign{camp("ativa", fixedNow.Add(-day), 3)}, nil)

		got, err := NewCampaignService(repo, clock).KeepOneActive(ctx)

		require.NoError(t, err)
		assert.Nil(t, got)
		repo.AssertNotCalled(t, "Update", anyArg, anyArg)
	})

	t.Run("reabre a que acabou por último, com a mesma duração; a futura fica", func(t *testing.T) {
		old := camp("esquenta", fixedNow.Add(-20*day), 7)
		last := camp("black-friday", fixedNow.Add(-6*day), 5)
		future := camp("natal", fixedNow.Add(20*day), 10)
		repo := &campaignRepoMock{}
		repo.On("ListActive", anyArg, anyArg).Return([]domain.Campaign{}, nil)
		repo.On("List", anyArg, "", 100).Return(domain.Page[domain.Campaign]{Items: []domain.Campaign{old, last, future}}, nil)
		start := fixedNow.UTC().Truncate(time.Hour)
		repo.On("OverlappingProducts", anyArg, last.ID, start, start.Add(5*day), []uuid.UUID(nil)).Return([]uuid.UUID{}, nil)
		repo.On("Update", anyArg, mock.MatchedBy(func(c *domain.Campaign) bool {
			return c.ID == last.ID && c.Slug == "black-friday" && c.StartsAt.Equal(start) && c.EndsAt.Equal(start.Add(5*day))
		})).Return(nil)
		repo.On("GetByID", anyArg, last.ID).Return(last, nil)

		got, err := NewCampaignService(repo, clock).KeepOneActive(ctx)

		require.NoError(t, err)
		require.NotNil(t, got)
		assert.Equal(t, last.ID, got.ID)
		repo.AssertNumberOfCalls(t, "Update", 1)
	})

	t.Run("se os produtos cruzam outra campanha, tenta a seguinte", func(t *testing.T) {
		last := camp("black-friday", fixedNow.Add(-6*day), 5)
		older := camp("esquenta", fixedNow.Add(-20*day), 7)
		repo := &campaignRepoMock{}
		repo.On("ListActive", anyArg, anyArg).Return([]domain.Campaign{}, nil)
		repo.On("List", anyArg, "", 100).Return(domain.Page[domain.Campaign]{Items: []domain.Campaign{older, last}}, nil)
		repo.On("OverlappingProducts", anyArg, last.ID, anyArg, anyArg, anyArg).Return([]uuid.UUID{uuid.New()}, nil)
		repo.On("OverlappingProducts", anyArg, older.ID, anyArg, anyArg, anyArg).Return([]uuid.UUID{}, nil)
		repo.On("Update", anyArg, anyArg).Return(nil)
		repo.On("GetByID", anyArg, older.ID).Return(older, nil)

		got, err := NewCampaignService(repo, clock).KeepOneActive(ctx)

		require.NoError(t, err)
		assert.Equal(t, older.ID, got.ID)
	})

	t.Run("sem campanha que já acabou, nada a reabrir", func(t *testing.T) {
		repo := &campaignRepoMock{}
		repo.On("ListActive", anyArg, anyArg).Return([]domain.Campaign{}, nil)
		repo.On("List", anyArg, "", 100).Return(domain.Page[domain.Campaign]{Items: []domain.Campaign{camp("natal", fixedNow.Add(20*day), 10)}}, nil)

		got, err := NewCampaignService(repo, clock).KeepOneActive(ctx)

		require.NoError(t, err)
		assert.Nil(t, got)
	})
}
