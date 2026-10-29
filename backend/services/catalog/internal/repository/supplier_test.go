package repository

import (
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

func supplierProduct(t *testing.T, vid string, categoryID uuid.UUID) domain.Product {
	t.Helper()
	p := domain.Product{
		SKU: vid, Name: "Wireless Headphones", CategoryID: categoryID, PriceCents: 12090, Status: domain.ProductActive,
		Supplier: &domain.SupplierInfo{Name: "cj", VID: vid, CostCents: 1240, CostCurrency: "USD", SyncedAt: now},
	}
	require.NoError(t, NewProductRepository(testDB).Create(ctx, &p))
	return p
}

func TestProduct_FromSupplier(t *testing.T) {
	resetDB(t)
	cat := mustCategory(t, "fones", nil)
	repo := NewProductRepository(testDB)
	created := supplierProduct(t, "SIM-V-001", cat.ID)
	own := mustProduct(t, "LOJA-1", cat.ID)

	got, err := repo.FindBySupplier(ctx, "cj", "SIM-V-001", now)
	require.NoError(t, err)
	require.NotNil(t, got.Supplier)
	assert.Equal(t, domain.SupplierInfo{Name: "cj", VID: "SIM-V-001", CostCents: 1240, CostCurrency: "USD", SyncedAt: now}, normalizeSupplier(*got.Supplier))
	loja, err := repo.GetByID(ctx, own.ID, now)
	require.NoError(t, err)
	assert.Nil(t, loja.Supplier, "produto da loja não tem fornecedor")

	_, err = repo.FindBySupplier(ctx, "cj", "SIM-V-999", now)
	require.ErrorIs(t, err, domain.ErrNotFound)

	// custo novo: preço novo, sem mexer no que é da loja
	got.Supplier.CostCents, got.Supplier.SyncedAt, got.PriceCents = 1590, now.Add(time.Minute), 15490
	got.Name = "não deve gravar"
	require.NoError(t, repo.UpdateFromSupplier(ctx, &got, created.Version))
	after, err := repo.GetByID(ctx, created.ID, now)
	require.NoError(t, err)
	assert.Equal(t, int64(15490), after.PriceCents)
	assert.Equal(t, int64(1590), after.Supplier.CostCents)
	assert.Equal(t, "Wireless Headphones", after.Name)
	assert.Equal(t, created.Version+1, after.Version)
	assert.Equal(t, []string{"ProductCreated", "ProductCreated", "ProductUpdated", "ProductPriceChanged"}, outboxTypes(t))

	require.ErrorIs(t, repo.UpdateFromSupplier(ctx, &after, created.Version), domain.ErrVersionConflict)

	list, err := repo.ListFromSuppliers(ctx, uuid.Nil, 10)
	require.NoError(t, err)
	assert.Equal(t, []string{"SIM-V-001"}, skus(list))

	// removido pela equipe: não volta pelo feed
	require.NoError(t, repo.Delete(ctx, created.ID))
	_, err = repo.FindBySupplier(ctx, "cj", "SIM-V-001", now)
	require.ErrorIs(t, err, domain.ErrDeleted)
}

func TestPricingRules(t *testing.T) {
	resetDB(t)
	cat := mustCategory(t, "fones", nil)
	rules := NewPricingRuleRepository(testDB)

	require.NoError(t, rules.Upsert(ctx, &domain.PricingRule{MarginPercent: 80, MinPriceCents: 1990}))
	require.NoError(t, rules.Upsert(ctx, &domain.PricingRule{MarginPercent: 70, MinPriceCents: 990})) // a padrão é uma só
	require.NoError(t, rules.Upsert(ctx, &domain.PricingRule{CategoryID: &cat.ID, MarginPercent: 120, MinPriceCents: 2990}))
	missing := uuid.New()
	require.ErrorIs(t, rules.Upsert(ctx, &domain.PricingRule{CategoryID: &missing, MarginPercent: 1}), domain.ErrInvalidReference)

	all, err := rules.List(ctx)
	require.NoError(t, err)
	require.Len(t, all, 2)
	byCategory := map[bool]domain.PricingRule{}
	for _, r := range all {
		byCategory[r.CategoryID != nil] = r
	}
	assert.Equal(t, 70, byCategory[false].MarginPercent)
	assert.Equal(t, 120, byCategory[true].MarginPercent)

	require.NoError(t, rules.Delete(ctx, cat.ID))
	require.ErrorIs(t, rules.Delete(ctx, cat.ID), domain.ErrNotFound)
}

func normalizeSupplier(s domain.SupplierInfo) domain.SupplierInfo {
	s.SyncedAt = s.SyncedAt.UTC()
	return s
}

func outboxTypes(t *testing.T) []string {
	t.Helper()
	var types []string
	require.NoError(t, testDB.Raw("SELECT event_type FROM outbox ORDER BY id").Scan(&types).Error)
	return types
}
