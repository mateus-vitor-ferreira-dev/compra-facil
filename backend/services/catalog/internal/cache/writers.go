package cache

import (
	"context"

	"github.com/google/uuid"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/service"
)

// Os writers envolvem os services nas escritas admin e invalidam o cache quando a escrita dá
// certo. As leituras admin (AdminList, AdminGet) passam direto, sem cache.

type invalidator interface {
	Invalidate(ctx context.Context)
}

type ProductWriter struct {
	*service.ProductService
	cache invalidator
}

func NewProductWriter(s *service.ProductService, c invalidator) *ProductWriter {
	return &ProductWriter{ProductService: s, cache: c}
}

func (w *ProductWriter) Create(ctx context.Context, in service.ProductInput) (domain.Product, error) {
	v, err := w.ProductService.Create(ctx, in)
	return invalidateOnSuccess(ctx, w.cache, v, err)
}

func (w *ProductWriter) Update(ctx context.Context, id uuid.UUID, in service.ProductInput, expectedVersion int) (domain.Product, error) {
	v, err := w.ProductService.Update(ctx, id, in, expectedVersion)
	return invalidateOnSuccess(ctx, w.cache, v, err)
}

func (w *ProductWriter) Delete(ctx context.Context, id uuid.UUID) error {
	return invalidateErr(ctx, w.cache, w.ProductService.Delete(ctx, id))
}

type CategoryWriter struct {
	*service.CategoryService
	cache invalidator
}

func NewCategoryWriter(s *service.CategoryService, c invalidator) *CategoryWriter {
	return &CategoryWriter{CategoryService: s, cache: c}
}

func (w *CategoryWriter) Create(ctx context.Context, in service.CategoryInput) (domain.Category, error) {
	v, err := w.CategoryService.Create(ctx, in)
	return invalidateOnSuccess(ctx, w.cache, v, err)
}

type CampaignWriter struct {
	*service.CampaignService
	cache invalidator
}

func NewCampaignWriter(s *service.CampaignService, c invalidator) *CampaignWriter {
	return &CampaignWriter{CampaignService: s, cache: c}
}

func (w *CampaignWriter) Create(ctx context.Context, in service.CampaignInput) (domain.Campaign, error) {
	v, err := w.CampaignService.Create(ctx, in)
	return invalidateOnSuccess(ctx, w.cache, v, err)
}

func (w *CampaignWriter) Update(ctx context.Context, id uuid.UUID, in service.CampaignInput) (domain.Campaign, error) {
	v, err := w.CampaignService.Update(ctx, id, in)
	return invalidateOnSuccess(ctx, w.cache, v, err)
}

func (w *CampaignWriter) Delete(ctx context.Context, id uuid.UUID) error {
	return invalidateErr(ctx, w.cache, w.CampaignService.Delete(ctx, id))
}

func (w *CampaignWriter) SetProducts(ctx context.Context, id uuid.UUID, productIDs []uuid.UUID) error {
	return invalidateErr(ctx, w.cache, w.CampaignService.SetProducts(ctx, id, productIDs))
}

func invalidateOnSuccess[T any](ctx context.Context, c invalidator, v T, err error) (T, error) {
	return v, invalidateErr(ctx, c, err)
}

func invalidateErr(ctx context.Context, c invalidator, err error) error {
	if err == nil {
		c.Invalidate(ctx)
	}
	return err
}
