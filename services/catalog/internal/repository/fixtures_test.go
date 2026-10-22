package repository

import (
	"context"
	"fmt"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
)

var (
	ctx = context.Background()
	// instante fixo: as campanhas dos testes são relativas a ele
	now = time.Date(2026, 11, 27, 12, 0, 0, 0, time.UTC)
)

func mustCategory(t *testing.T, slug string, parent *uuid.UUID) domain.Category {
	t.Helper()
	c := domain.Category{Slug: slug, Name: "Cat " + slug, ParentID: parent}
	require.NoError(t, NewCategoryRepository(testDB).Create(ctx, &c))
	return c
}

type productOpt func(*domain.Product)

func withName(name string) productOpt  { return func(p *domain.Product) { p.Name = name } }
func withPrice(cents int64) productOpt { return func(p *domain.Product) { p.PriceCents = cents } }
func withStatus(s domain.ProductStatus) productOpt {
	return func(p *domain.Product) { p.Status = s }
}

func mustProduct(t *testing.T, sku string, categoryID uuid.UUID, opts ...productOpt) domain.Product {
	t.Helper()
	p := domain.Product{
		SKU: sku, Name: "Produto " + sku, Description: "descrição", CategoryID: categoryID,
		PriceCents: 10000, Status: domain.ProductActive,
	}
	for _, o := range opts {
		o(&p)
	}
	require.NoError(t, NewProductRepository(testDB).Create(ctx, &p))
	return p
}

func mustCampaign(t *testing.T, slug string, discount int, start, end time.Time, products ...uuid.UUID) domain.Campaign {
	t.Helper()
	repo := NewCampaignRepository(testDB)
	c := domain.Campaign{Slug: slug, Name: "Campanha " + slug, DiscountPercent: discount, StartsAt: start, EndsAt: end}
	require.NoError(t, repo.Create(ctx, &c))
	require.NoError(t, repo.SetProducts(ctx, c.ID, products))
	return c
}

func skus(items []domain.Product) []string {
	out := make([]string, len(items))
	for i, p := range items {
		out[i] = p.SKU
	}
	return out
}

func seqSKU(i int) string { return fmt.Sprintf("SKU-%03d", i) }
