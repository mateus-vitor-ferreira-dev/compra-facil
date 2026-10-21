package service

import (
	"errors"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/mock"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
)

func campaignInput() CampaignInput {
	return CampaignInput{
		Slug: "black-friday", Name: "Black Friday", DiscountPercent: 30,
		StartsAt: fixedNow, EndsAt: fixedNow.Add(24 * time.Hour),
	}
}

func TestCampaignService_Create(t *testing.T) {
	t.Run("grava em UTC", func(t *testing.T) {
		repo := &campaignRepoMock{}
		svc := NewCampaignService(repo, clock)
		in := campaignInput()
		in.StartsAt = in.StartsAt.In(time.FixedZone("BRT", -3*3600))
		repo.On("Create", anyArg, mock.MatchedBy(func(c *domain.Campaign) bool {
			return c.StartsAt.Location() == time.UTC && c.StartsAt.Equal(fixedNow)
		})).Return(nil)

		_, err := svc.Create(ctx, in)
		require.NoError(t, err)
	})
	t.Run("período invertido", func(t *testing.T) {
		svc := NewCampaignService(&campaignRepoMock{}, clock)
		in := campaignInput()
		in.EndsAt = in.StartsAt.Add(-time.Hour)

		_, err := svc.Create(ctx, in)
		assert.Equal(t, []string{"ends_at"}, validationFields(t, err))
	})
	t.Run("slug duplicado", func(t *testing.T) {
		repo := &campaignRepoMock{}
		repo.On("Create", anyArg, anyArg).Return(domain.ErrConflict)

		_, err := NewCampaignService(repo, clock).Create(ctx, campaignInput())
		require.ErrorIs(t, err, domain.ErrConflict)
		assert.Contains(t, err.Error(), "slug black-friday")
	})
}

func TestCampaignService_Update(t *testing.T) {
	id := uuid.New()
	in := campaignInput()

	t.Run("checa sobreposição com os produtos atuais", func(t *testing.T) {
		repo := &campaignRepoMock{}
		repo.On("OverlappingProducts", anyArg, id, in.StartsAt, in.EndsAt, []uuid.UUID(nil)).Return([]uuid.UUID{}, nil)
		repo.On("Update", anyArg, anyArg).Return(nil)
		repo.On("GetByID", anyArg, id).Return(domain.Campaign{ID: id, ProductCount: 3}, nil)

		got, err := NewCampaignService(repo, clock).Update(ctx, id, in)

		require.NoError(t, err)
		assert.Equal(t, 3, got.ProductCount)
	})
	t.Run("novas datas cruzam outra campanha", func(t *testing.T) {
		repo := &campaignRepoMock{}
		clash := uuid.New()
		repo.On("OverlappingProducts", anyArg, id, anyArg, anyArg, anyArg).Return([]uuid.UUID{clash}, nil)

		_, err := NewCampaignService(repo, clock).Update(ctx, id, in)

		require.ErrorIs(t, err, domain.ErrConflict)
		assert.Contains(t, err.Error(), clash.String())
		repo.AssertNotCalled(t, "Update", anyArg, anyArg)
	})
	t.Run("inexistente", func(t *testing.T) {
		repo := &campaignRepoMock{}
		repo.On("OverlappingProducts", anyArg, id, anyArg, anyArg, anyArg).Return([]uuid.UUID{}, nil)
		repo.On("Update", anyArg, anyArg).Return(domain.ErrNotFound)

		_, err := NewCampaignService(repo, clock).Update(ctx, id, in)
		assert.ErrorIs(t, err, domain.ErrNotFound)
	})
}

func TestCampaignService_SetProducts(t *testing.T) {
	id := uuid.New()
	camp := domain.Campaign{ID: id, StartsAt: fixedNow, EndsAt: fixedNow.Add(time.Hour)}
	a, b := uuid.New(), uuid.New()

	t.Run("ok", func(t *testing.T) {
		repo := &campaignRepoMock{}
		repo.On("GetByID", anyArg, id).Return(camp, nil)
		repo.On("OverlappingProducts", anyArg, id, camp.StartsAt, camp.EndsAt, []uuid.UUID{a, b}).Return([]uuid.UUID{}, nil)
		repo.On("SetProducts", anyArg, id, []uuid.UUID{a, b}).Return(nil)

		require.NoError(t, NewCampaignService(repo, clock).SetProducts(ctx, id, []uuid.UUID{a, b}))
		repo.AssertExpectations(t)
	})
	t.Run("lista nil esvazia a campanha sem checar a lista atual", func(t *testing.T) {
		repo := &campaignRepoMock{}
		repo.On("GetByID", anyArg, id).Return(camp, nil)
		repo.On("OverlappingProducts", anyArg, id, anyArg, anyArg, []uuid.UUID{}).Return([]uuid.UUID{}, nil)
		repo.On("SetProducts", anyArg, id, []uuid.UUID{}).Return(nil)

		require.NoError(t, NewCampaignService(repo, clock).SetProducts(ctx, id, nil))
	})

	failures := []struct {
		name  string
		ids   []uuid.UUID
		setup func(*campaignRepoMock)
		check func(t *testing.T, err error)
	}{
		{"repetido", []uuid.UUID{a, a}, func(*campaignRepoMock) {}, func(t *testing.T, err error) {
			assert.Equal(t, []string{"product_ids"}, validationFields(t, err))
		}},
		{"acima do limite", make([]uuid.UUID, domain.MaxCampaignItems+1), func(*campaignRepoMock) {}, func(t *testing.T, err error) {
			assert.Equal(t, []string{"product_ids"}, validationFields(t, err))
		}},
		{"campanha inexistente", []uuid.UUID{a}, func(r *campaignRepoMock) {
			r.On("GetByID", anyArg, id).Return(domain.Campaign{}, domain.ErrNotFound)
		}, func(t *testing.T, err error) { assert.ErrorIs(t, err, domain.ErrNotFound) }},
		{"produto em outra campanha", []uuid.UUID{a}, func(r *campaignRepoMock) {
			r.On("GetByID", anyArg, id).Return(camp, nil)
			r.On("OverlappingProducts", anyArg, id, anyArg, anyArg, anyArg).Return([]uuid.UUID{a}, nil)
		}, func(t *testing.T, err error) { assert.ErrorIs(t, err, domain.ErrConflict) }},
		{"produto inexistente", []uuid.UUID{a}, func(r *campaignRepoMock) {
			r.On("GetByID", anyArg, id).Return(camp, nil)
			r.On("OverlappingProducts", anyArg, id, anyArg, anyArg, anyArg).Return([]uuid.UUID{}, nil)
			r.On("SetProducts", anyArg, id, anyArg).Return(domain.ErrInvalidReference)
		}, func(t *testing.T, err error) {
			assert.Equal(t, []string{"product_ids"}, validationFields(t, err))
		}},
		{"falha ao checar sobreposição", []uuid.UUID{a}, func(r *campaignRepoMock) {
			r.On("GetByID", anyArg, id).Return(camp, nil)
			r.On("OverlappingProducts", anyArg, id, anyArg, anyArg, anyArg).Return([]uuid.UUID(nil), errors.New("timeout"))
		}, func(t *testing.T, err error) { assert.ErrorContains(t, err, "timeout") }},
	}
	for _, tt := range failures {
		t.Run(tt.name, func(t *testing.T) {
			repo := &campaignRepoMock{}
			tt.setup(repo)
			err := NewCampaignService(repo, clock).SetProducts(ctx, id, tt.ids)
			tt.check(t, err)
			repo.AssertNotCalled(t, "SetProducts", anyArg, id, []uuid.UUID{a, a})
		})
	}
}

func TestCampaignService_Reads(t *testing.T) {
	repo := &campaignRepoMock{}
	svc := NewCampaignService(repo, clock)
	id := uuid.New()
	repo.On("ListActive", anyArg, fixedNow).Return([]domain.Campaign{{ID: id}}, nil)
	repo.On("List", anyArg, "", 0).Return(domain.Page[domain.Campaign]{}, nil)
	repo.On("List", anyArg, "lixo", 0).Return(domain.Page[domain.Campaign]{}, domain.ErrInvalidCursor)
	repo.On("Delete", anyArg, id).Return(nil)

	active, err := svc.ListActive(ctx)
	require.NoError(t, err)
	assert.Len(t, active, 1)

	_, err = svc.AdminList(ctx, "", 0)
	require.NoError(t, err)

	_, err = svc.AdminList(ctx, "lixo", 0)
	assert.Equal(t, []string{"cursor"}, validationFields(t, err))

	_, err = svc.AdminList(ctx, "", 1000)
	assert.Equal(t, []string{"limit"}, validationFields(t, err))

	require.NoError(t, svc.Delete(ctx, id))
}

func TestCategoryService(t *testing.T) {
	parent := uuid.New()

	t.Run("cria normalizando", func(t *testing.T) {
		repo := &categoryRepoMock{}
		repo.On("Create", anyArg, mock.MatchedBy(func(c *domain.Category) bool { return c.Slug == "tv" && c.Name == "TVs" })).Return(nil)

		got, err := NewCategoryService(repo).Create(ctx, CategoryInput{Slug: " tv ", Name: " TVs ", ParentID: &parent})
		require.NoError(t, err)
		assert.Equal(t, &parent, got.ParentID)
	})

	cases := []struct {
		name    string
		in      CategoryInput
		repoErr error
		check   func(t *testing.T, err error)
	}{
		{"slug inválido", CategoryInput{Slug: "TV!", Name: "TVs"}, nil, func(t *testing.T, err error) {
			assert.Equal(t, []string{"slug"}, validationFields(t, err))
		}},
		{"pai inexistente", CategoryInput{Slug: "tv", Name: "TVs", ParentID: &parent}, domain.ErrInvalidReference, func(t *testing.T, err error) {
			assert.Equal(t, []string{"parent_id"}, validationFields(t, err))
		}},
		{"slug duplicado", CategoryInput{Slug: "tv", Name: "TVs"}, domain.ErrConflict, func(t *testing.T, err error) {
			assert.ErrorIs(t, err, domain.ErrConflict)
		}},
		{"erro de banco", CategoryInput{Slug: "tv", Name: "TVs"}, errors.New("boom"), func(t *testing.T, err error) {
			assert.ErrorContains(t, err, "boom")
		}},
	}
	for _, tt := range cases {
		t.Run(tt.name, func(t *testing.T) {
			repo := &categoryRepoMock{}
			repo.On("Create", anyArg, anyArg).Return(tt.repoErr)
			_, err := NewCategoryService(repo).Create(ctx, tt.in)
			tt.check(t, err)
		})
	}

	t.Run("lista", func(t *testing.T) {
		repo := &categoryRepoMock{}
		repo.On("List", anyArg).Return([]domain.Category{{Slug: "tv"}}, nil)
		got, err := NewCategoryService(repo).List(ctx)
		require.NoError(t, err)
		assert.Len(t, got, 1)
	})
}
