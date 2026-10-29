package handler

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/auth"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/messaging"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/service"
)

type fakePricing struct {
	settings service.PricingSettings
	gotRule  domain.PricingRule
	gotID    uuid.UUID
	repriced int
	err      error
}

func (f *fakePricing) Settings(context.Context) (service.PricingSettings, error) {
	return f.settings, f.err
}

func (f *fakePricing) SetRule(_ context.Context, r domain.PricingRule) (domain.PricingRule, int, error) {
	f.gotRule = r
	r.UpdatedAt = time.Date(2026, 10, 29, 12, 0, 0, 0, time.UTC)
	return r, f.repriced, f.err
}

func (f *fakePricing) DeleteRule(_ context.Context, id uuid.UUID) (int, error) {
	f.gotID = id
	return f.repriced, f.err
}

type fakeDeadLetters struct {
	items []messaging.DeadLetter
	err   error
}

func (f *fakeDeadLetters) Pending(context.Context, int) ([]messaging.DeadLetter, error) {
	return f.items, f.err
}
func (f *fakeDeadLetters) Replay(context.Context) (int, error) { return len(f.items), f.err }

func newSupplierAPI(t *testing.T, dlt DeadLetterAdmin) (http.Handler, *fakePricing, *int) {
	t.Helper()
	pricing := &fakePricing{}
	invalidated := 0
	h := NewSupplierAdminHandler(pricing, dlt, func(context.Context) { invalidated++ },
		RequireRole(testIdentity.Verifier(t), auth.RoleAdmin, discardLogger()), discardLogger())
	return NewRouter(discardLogger(), NewHealth(), h), pricing, &invalidated
}

func TestSupplier_GetPricing(t *testing.T) {
	api, pricing, _ := newSupplierAPI(t, nil)
	cat := uuid.New()
	pricing.settings = service.PricingSettings{
		Default: domain.PricingRule{MarginPercent: 80, MinPriceCents: 1990}, // a da configuração
		Rules:   []domain.PricingRule{{CategoryID: &cat, MarginPercent: 120, MinPriceCents: 0, UpdatedAt: time.Now()}},
		Rates:   domain.ExchangeRates{"USD": 5400},
	}

	w := send(t, api, http.MethodGet, "/v1/admin/pricing-rules", "", adminHeaders(t), true)

	require.Equal(t, http.StatusOK, w.Code)
	assert.JSONEq(t, `"5.40"`, mustField(t, w.Body.Bytes(), "exchange_rates", "USD"))
	assert.JSONEq(t, `null`, mustField(t, w.Body.Bytes(), "default", "updated_at"))
}

func TestSupplier_SetRules_RepriceAndInvalidateTheCache(t *testing.T) {
	api, pricing, invalidated := newSupplierAPI(t, nil)
	pricing.repriced = 3
	cat := uuid.New()

	w := send(t, api, http.MethodPut, "/v1/admin/pricing-rules/categories/"+cat.String(),
		`{"margin_percent":120,"min_price_cents":2990}`, adminHeaders(t), true)
	require.Equal(t, http.StatusOK, w.Code)
	assert.Equal(t, domain.PricingRule{CategoryID: &cat, MarginPercent: 120, MinPriceCents: 2990}, pricing.gotRule)
	assert.JSONEq(t, `3`, mustField(t, w.Body.Bytes(), "repriced"))
	assert.Equal(t, 1, *invalidated)

	w = send(t, api, http.MethodPut, "/v1/admin/pricing-rules/default", `{"margin_percent":70,"min_price_cents":1990}`, adminHeaders(t), true)
	require.Equal(t, http.StatusOK, w.Code)
	assert.Nil(t, pricing.gotRule.CategoryID)

	w = send(t, api, http.MethodPut, "/v1/admin/pricing-rules/default", `{"margin_percent":70}`, adminHeaders(t), false)
	assert.Equal(t, http.StatusUnprocessableEntity, w.Code, "sem min_price_cents")

	pricing.err = domain.Invalid("margin_percent", "deve estar entre 0 e 1000")
	pricing.repriced = 0
	w = send(t, api, http.MethodPut, "/v1/admin/pricing-rules/default", `{"margin_percent":5000,"min_price_cents":0}`, adminHeaders(t), false)
	assert.Equal(t, http.StatusUnprocessableEntity, w.Code)

	pricing.err = domain.ErrNotFound
	w = send(t, api, http.MethodDelete, "/v1/admin/pricing-rules/categories/"+cat.String(), "", adminHeaders(t), true)
	assert.Equal(t, http.StatusNotFound, w.Code)
}

func TestSupplier_DeadLetters(t *testing.T) {
	eventType, failure := "SupplierProductUpdated", "categoria do fornecedor sem categoria da loja"
	id, partition, offset := uuid.New(), 1, int64(42)
	at := time.Now().UTC()
	dlt := &fakeDeadLetters{items: []messaging.DeadLetter{{EventID: &id, EventType: &eventType, Topic: "fulfillment.supplier-product.v1",
		Partition: &partition, Offset: &offset, Attempts: 4, Error: &failure, FailedAt: &at}}}
	api, _, _ := newSupplierAPI(t, dlt)

	w := send(t, api, http.MethodGet, "/v1/admin/dead-letters?limit=10", "", adminHeaders(t), true)
	require.Equal(t, http.StatusOK, w.Code)
	assert.Contains(t, w.Body.String(), "fulfillment.supplier-product.v1")

	w = send(t, api, http.MethodPost, "/v1/admin/dead-letters/replay", "", adminHeaders(t), true)
	require.Equal(t, http.StatusOK, w.Code)
	assert.JSONEq(t, `1`, mustField(t, w.Body.Bytes(), "replayed"))

	dlt.err = errors.New("kafka fora")
	w = send(t, api, http.MethodGet, "/v1/admin/dead-letters", "", adminHeaders(t), true)
	assert.Equal(t, http.StatusServiceUnavailable, w.Code)

	off, _, _ := newSupplierAPI(t, nil) // consumo do feed desligado
	w = send(t, off, http.MethodPost, "/v1/admin/dead-letters/replay", "", adminHeaders(t), true)
	assert.Equal(t, http.StatusServiceUnavailable, w.Code)
}

func TestSupplier_RoutesRequireAuth(t *testing.T) {
	api, _, _ := newSupplierAPI(t, nil)
	id := uuid.NewString()
	for _, r := range [][2]string{
		{http.MethodGet, "/v1/admin/pricing-rules"},
		{http.MethodPut, "/v1/admin/pricing-rules/default"},
		{http.MethodPut, "/v1/admin/pricing-rules/categories/" + id},
		{http.MethodDelete, "/v1/admin/pricing-rules/categories/" + id},
		{http.MethodGet, "/v1/admin/dead-letters"},
		{http.MethodPost, "/v1/admin/dead-letters/replay"},
	} {
		w := send(t, api, r[0], r[1], "", nil, false)
		assert.Equal(t, http.StatusUnauthorized, w.Code, "%s %s", r[0], r[1])
	}
}

func TestFormatRate(t *testing.T) {
	assert.Equal(t, "5.40", formatRate(5400))
	assert.Equal(t, "0.183", formatRate(183))
	assert.Equal(t, "5.00", formatRate(5000))
}

// mustField devolve o JSON cru de um campo (aninhado pelo caminho) da resposta.
func mustField(t *testing.T, body []byte, path ...string) string {
	t.Helper()
	var node any
	require.NoError(t, json.Unmarshal(body, &node))
	for _, key := range path {
		m, ok := node.(map[string]any)
		require.True(t, ok, "%s não é objeto", key)
		node = m[key]
	}
	raw, err := json.Marshal(node)
	require.NoError(t, err)
	return string(raw)
}
