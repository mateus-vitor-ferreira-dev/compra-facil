package service

import (
	"context"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/mock"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

// Mocks dos ports (testify/mock). Os testes de integração dos repositórios ficam em repository/.

type productRepoMock struct{ mock.Mock }

func (m *productRepoMock) Create(ctx context.Context, p *domain.Product) error {
	args := m.Called(ctx, p)
	if args.Error(0) == nil {
		p.ID, p.Version = uuid.New(), 1
	}
	return args.Error(0)
}

func (m *productRepoMock) Update(ctx context.Context, p *domain.Product, expectedVersion int) error {
	return m.Called(ctx, p, expectedVersion).Error(0)
}

func (m *productRepoMock) Delete(ctx context.Context, id uuid.UUID) error {
	return m.Called(ctx, id).Error(0)
}

func (m *productRepoMock) GetByID(ctx context.Context, id uuid.UUID, now time.Time) (domain.Product, error) {
	args := m.Called(ctx, id, now)
	return args.Get(0).(domain.Product), args.Error(1)
}

func (m *productRepoMock) GetByIDs(ctx context.Context, ids []uuid.UUID, now time.Time) ([]domain.Product, error) {
	args := m.Called(ctx, ids, now)
	return args.Get(0).([]domain.Product), args.Error(1)
}

func (m *productRepoMock) List(ctx context.Context, f domain.ProductFilter) (domain.Page[domain.Product], error) {
	args := m.Called(ctx, f)
	return args.Get(0).(domain.Page[domain.Product]), args.Error(1)
}

func (m *productRepoMock) Brands(ctx context.Context, f domain.ProductFilter, limit int) ([]domain.BrandCount, error) {
	args := m.Called(ctx, f, limit)
	return args.Get(0).([]domain.BrandCount), args.Error(1)
}

func (m *productRepoMock) CountByCategory(ctx context.Context, f domain.ProductFilter) (map[uuid.UUID]int, error) {
	args := m.Called(ctx, f)
	return args.Get(0).(map[uuid.UUID]int), args.Error(1)
}

type categoryRepoMock struct{ mock.Mock }

func (m *categoryRepoMock) Create(ctx context.Context, c *domain.Category) error {
	return m.Called(ctx, c).Error(0)
}

func (m *categoryRepoMock) List(ctx context.Context) ([]domain.Category, error) {
	args := m.Called(ctx)
	return args.Get(0).([]domain.Category), args.Error(1)
}

func (m *categoryRepoMock) GetBySlug(ctx context.Context, slug string) (domain.Category, error) {
	args := m.Called(ctx, slug)
	return args.Get(0).(domain.Category), args.Error(1)
}

func (m *categoryRepoMock) SubtreeIDs(ctx context.Context, id uuid.UUID) ([]uuid.UUID, error) {
	args := m.Called(ctx, id)
	return args.Get(0).([]uuid.UUID), args.Error(1)
}

type campaignRepoMock struct{ mock.Mock }

func (m *campaignRepoMock) Create(ctx context.Context, c *domain.Campaign) error {
	return m.Called(ctx, c).Error(0)
}

func (m *campaignRepoMock) Update(ctx context.Context, c *domain.Campaign) error {
	return m.Called(ctx, c).Error(0)
}

func (m *campaignRepoMock) Delete(ctx context.Context, id uuid.UUID) error {
	return m.Called(ctx, id).Error(0)
}

func (m *campaignRepoMock) GetByID(ctx context.Context, id uuid.UUID) (domain.Campaign, error) {
	args := m.Called(ctx, id)
	return args.Get(0).(domain.Campaign), args.Error(1)
}

func (m *campaignRepoMock) ListActive(ctx context.Context, now time.Time) ([]domain.Campaign, error) {
	args := m.Called(ctx, now)
	return args.Get(0).([]domain.Campaign), args.Error(1)
}

func (m *campaignRepoMock) NextStart(ctx context.Context, now time.Time) (time.Time, error) {
	args := m.Called(ctx, now)
	return args.Get(0).(time.Time), args.Error(1)
}

func (m *campaignRepoMock) List(ctx context.Context, cursor string, limit int) (domain.Page[domain.Campaign], error) {
	args := m.Called(ctx, cursor, limit)
	return args.Get(0).(domain.Page[domain.Campaign]), args.Error(1)
}

func (m *campaignRepoMock) ProductIDs(ctx context.Context, id uuid.UUID) ([]uuid.UUID, error) {
	args := m.Called(ctx, id)
	return args.Get(0).([]uuid.UUID), args.Error(1)
}

func (m *campaignRepoMock) SetProducts(ctx context.Context, id uuid.UUID, ids []uuid.UUID) error {
	return m.Called(ctx, id, ids).Error(0)
}

func (m *campaignRepoMock) OverlappingProducts(
	ctx context.Context, id uuid.UUID, start, end time.Time, ids []uuid.UUID,
) ([]uuid.UUID, error) {
	args := m.Called(ctx, id, start, end, ids)
	return args.Get(0).([]uuid.UUID), args.Error(1)
}

var (
	ctx      = context.Background()
	anyArg   = mock.Anything
	fixedNow = time.Date(2026, 11, 27, 12, 0, 0, 0, time.UTC)
	clock    = func() time.Time { return fixedNow }
)
