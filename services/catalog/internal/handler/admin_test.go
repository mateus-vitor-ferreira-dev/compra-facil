package handler

import (
	"context"
	"fmt"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/golang-jwt/jwt/v5"
	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/auth"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/auth/authtest"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/service"
)

// ---------- fakes dos services admin ----------

type fakeProductAdmin struct {
	page        domain.Page[domain.Product]
	product     domain.Product
	err         error
	gotList     service.AdminListInput
	gotIn       service.ProductInput
	gotID       uuid.UUID
	gotVersion  int
	deleteCalls int
}

func (f *fakeProductAdmin) AdminList(_ context.Context, in service.AdminListInput) (domain.Page[domain.Product], error) {
	f.gotList = in
	return f.page, f.err
}

func (f *fakeProductAdmin) AdminGet(_ context.Context, id uuid.UUID) (domain.Product, error) {
	f.gotID = id
	return f.product, f.err
}

func (f *fakeProductAdmin) Create(_ context.Context, in service.ProductInput) (domain.Product, error) {
	f.gotIn = in
	return f.product, f.err
}

func (f *fakeProductAdmin) Update(_ context.Context, id uuid.UUID, in service.ProductInput, v int) (domain.Product, error) {
	f.gotID, f.gotIn, f.gotVersion = id, in, v
	return f.product, f.err
}

func (f *fakeProductAdmin) Delete(_ context.Context, id uuid.UUID) error {
	f.gotID = id
	f.deleteCalls++
	return f.err
}

type fakeCategoryAdmin struct {
	cat   domain.Category
	err   error
	gotIn service.CategoryInput
}

func (f *fakeCategoryAdmin) Create(_ context.Context, in service.CategoryInput) (domain.Category, error) {
	f.gotIn = in
	return f.cat, f.err
}

type fakeCampaignAdmin struct {
	page      domain.Page[domain.Campaign]
	camp      domain.Campaign
	err       error
	gotCursor string
	gotLimit  int
	gotIn     service.CampaignInput
	gotID     uuid.UUID
	gotIDs    []uuid.UUID
}

func (f *fakeCampaignAdmin) AdminList(_ context.Context, cursor string, limit int) (domain.Page[domain.Campaign], error) {
	f.gotCursor, f.gotLimit = cursor, limit
	return f.page, f.err
}

func (f *fakeCampaignAdmin) Create(_ context.Context, in service.CampaignInput) (domain.Campaign, error) {
	f.gotIn = in
	return f.camp, f.err
}

func (f *fakeCampaignAdmin) Update(_ context.Context, id uuid.UUID, in service.CampaignInput) (domain.Campaign, error) {
	f.gotID, f.gotIn = id, in
	return f.camp, f.err
}

func (f *fakeCampaignAdmin) Delete(_ context.Context, id uuid.UUID) error {
	f.gotID = id
	return f.err
}

func (f *fakeCampaignAdmin) SetProducts(_ context.Context, id uuid.UUID, ids []uuid.UUID) error {
	f.gotID, f.gotIDs = id, ids
	return f.err
}

// ---------- montagem ----------

// testIdentity faz o papel do identity: as rotas admin validam os tokens dele pelo JWKS
var testIdentity = authtest.MustNew()

type adminFakes struct {
	products   *fakeProductAdmin
	categories *fakeCategoryAdmin
	campaigns  *fakeCampaignAdmin
}

func newAdminAPI(t *testing.T) (http.Handler, adminFakes) {
	t.Helper()
	v := testIdentity.Verifier(t)
	f := adminFakes{&fakeProductAdmin{}, &fakeCategoryAdmin{}, &fakeCampaignAdmin{}}
	admin := NewAdminHandler(f.products, f.categories, f.campaigns, RequireRole(v, auth.RoleAdmin, discardLogger()), discardLogger())
	return NewRouter(discardLogger(), NewHealth(), admin), f
}

func token(t *testing.T, roles ...string) string {
	t.Helper()
	return testIdentity.Token(t, roles)
}

func adminHeaders(t *testing.T, extra ...string) map[string]string {
	h := map[string]string{"Authorization": "Bearer " + token(t, auth.RoleAdmin)}
	for i := 0; i+1 < len(extra); i += 2 {
		h[extra[i]] = extra[i+1]
	}
	return h
}

// send faz a requisição com corpo JSON; com checkContract valida requisição e resposta no catalog.v1.
func send(t *testing.T, h http.Handler, method, path, body string, headers map[string]string, checkContract bool) *httptest.ResponseRecorder {
	t.Helper()
	newReq := func() *http.Request {
		req := httptest.NewRequestWithContext(context.Background(), method, "http://localhost:8001"+path, strings.NewReader(body))
		if body != "" {
			req.Header.Set("Content-Type", "application/json")
		}
		for k, v := range headers {
			req.Header.Set(k, v)
		}
		return req
	}
	w := httptest.NewRecorder()
	h.ServeHTTP(w, newReq())

	if checkContract {
		ok, errs := contract(t).ValidateHttpRequestResponse(newReq(), w.Result()) // o corpo já foi lido: requisição nova
		if !ok {
			for _, e := range errs {
				t.Errorf("fora do contrato: %s — %s", e.Message, e.Reason)
				for _, se := range e.SchemaValidationErrors {
					t.Errorf("  %s: %s", se.FieldPath, se.Reason)
				}
			}
			t.Logf("corpo: %s", w.Body.String())
		}
	}
	return w
}

const productBody = `{"sku":"TV-55","name":"Smart TV 55","description":"QLED","category_id":"%s",
	"image_url":"https://cdn.example.com/tv.jpg","price_cents":10000,"status":"active"}`

// ---------- autenticação ----------

func TestAdmin_Auth(t *testing.T) {
	api, f := newAdminAPI(t)
	f.products.page = domain.Page[domain.Product]{Items: []domain.Product{}}

	foreign := testIdentity.Token(t, []string{auth.RoleAdmin}, func(c *auth.Claims) { c.Issuer = "terceiro" })
	expired := testIdentity.Token(t, []string{auth.RoleAdmin}, func(c *auth.Claims) {
		c.ExpiresAt = jwt.NewNumericDate(time.Now().Add(-time.Minute))
	})
	forged := authtest.New(t).Token(t, []string{auth.RoleAdmin}) // outra chave, mesmo formato

	tests := []struct {
		name       string
		header     string
		wantStatus int
	}{
		{"sem Authorization", "", http.StatusUnauthorized},
		{"esquema errado", "Basic YWRtaW46YWRtaW4=", http.StatusUnauthorized},
		{"token inválido", "Bearer nao.e.jwt", http.StatusUnauthorized},
		{"token de outro emissor", "Bearer " + foreign, http.StatusUnauthorized},
		{"token vencido", "Bearer " + expired, http.StatusUnauthorized},
		{"assinado por outra chave", "Bearer " + forged, http.StatusUnauthorized},
		{"cliente (sem a role admin)", "Bearer " + token(t, auth.RoleCustomer), http.StatusForbidden},
		{"admin", "Bearer " + token(t, auth.RoleAdmin), http.StatusOK},
		{"esquema sem diferenciar maiúsculas", "bearer " + token(t, auth.RoleAdmin), http.StatusOK},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			headers := map[string]string{}
			if tt.header != "" {
				headers["Authorization"] = tt.header
			}
			// o validador recusaria a requisição sem credencial; aqui interessa a resposta
			w := send(t, api, http.MethodGet, "/v1/admin/products", "", headers, false)

			assert.Equal(t, tt.wantStatus, w.Code)
			if tt.wantStatus == http.StatusUnauthorized {
				assert.Equal(t, `Bearer realm="catalog"`, w.Header().Get("WWW-Authenticate"))
				assert.Equal(t, "application/problem+json", w.Header().Get("Content-Type"))
			}
		})
	}
}

func TestAdmin_AllRoutesRequireAuth(t *testing.T) {
	api, _ := newAdminAPI(t)
	id := uuid.NewString()
	routes := [][2]string{
		{http.MethodGet, "/v1/admin/products"},
		{http.MethodPost, "/v1/admin/products"},
		{http.MethodGet, "/v1/admin/products/" + id},
		{http.MethodPut, "/v1/admin/products/" + id},
		{http.MethodDelete, "/v1/admin/products/" + id},
		{http.MethodPost, "/v1/admin/categories"},
		{http.MethodGet, "/v1/admin/campaigns"},
		{http.MethodPost, "/v1/admin/campaigns"},
		{http.MethodPut, "/v1/admin/campaigns/" + id},
		{http.MethodDelete, "/v1/admin/campaigns/" + id},
		{http.MethodPut, "/v1/admin/campaigns/" + id + "/products"},
	}
	for _, r := range routes {
		w := send(t, api, r[0], r[1], "", nil, false)
		assert.Equal(t, http.StatusUnauthorized, w.Code, "%s %s", r[0], r[1])
	}
}

// ---------- produtos ----------

func TestAdmin_ListProducts(t *testing.T) {
	api, f := newAdminAPI(t)
	p := sampleProduct(false)
	p.Status = domain.ProductDraft
	f.products.page = domain.Page[domain.Product]{Items: []domain.Product{p}, NextCursor: "abc"}

	w := send(t, api, http.MethodGet, "/v1/admin/products?status=draft&sku=TV-55&limit=10&cursor=xyz", "", adminHeaders(t), true)

	require.Equal(t, http.StatusOK, w.Code)
	draft := domain.ProductDraft
	assert.Equal(t, service.AdminListInput{Status: &draft, SKU: "TV-55", Cursor: "xyz", Limit: 10}, f.products.gotList)
	first := decode(t, w)["data"].([]any)[0].(map[string]any)
	assert.Equal(t, "draft", first["status"])
	assert.InDelta(t, 3, first["version"], 0)
}

func TestAdmin_CreateProduct(t *testing.T) {
	api, f := newAdminAPI(t)
	p := sampleProduct(false)
	f.products.product = p
	catID := uuid.New()

	w := send(t, api, http.MethodPost, "/v1/admin/products", fmt.Sprintf(productBody, catID), adminHeaders(t), true)

	require.Equal(t, http.StatusCreated, w.Code)
	assert.Equal(t, "/v1/admin/products/"+p.ID.String(), w.Header().Get("Location"))
	assert.Equal(t, `"3"`, w.Header().Get("ETag"))
	img := "https://cdn.example.com/tv.jpg"
	assert.Equal(t, service.ProductInput{
		SKU: "TV-55", Name: "Smart TV 55", Description: "QLED", CategoryID: catID,
		ImageURL: &img, PriceCents: 10000, Status: domain.ProductActive,
	}, f.products.gotIn)
	assert.Equal(t, "active", decode(t, w)["status"])
}

func TestAdmin_CreateProduct_Errors(t *testing.T) {
	valid := fmt.Sprintf(productBody, uuid.New())
	tests := []struct {
		name       string
		body       string
		svcErr     error
		wantStatus int
		wantInBody string
	}{
		{"sem corpo", "", nil, http.StatusBadRequest, "corpo JSON obrigatório"},
		{"JSON malformado", `{"sku":`, nil, http.StatusBadRequest, "JSON malformado"},
		{"campo fora do contrato", `{"sku":"TV-55","preco":1}`, nil, http.StatusBadRequest, `campo desconhecido \"preco\"`},
		{"tipo errado", `{"price_cents":"dez"}`, nil, http.StatusBadRequest, "price_cents"},
		{"UUID inválido", `{"category_id":"abc"}`, nil, http.StatusBadRequest, "UUID inválido"},
		{"dois objetos", `{} {}`, nil, http.StatusBadRequest, "único objeto"},
		{
			"validação vira 422", valid, &domain.ValidationError{Fields: []domain.FieldError{{Field: "sku", Message: "inválido"}}},
			http.StatusUnprocessableEntity, `"field":"sku"`,
		},
		{"SKU repetido vira 409", valid, fmt.Errorf("%w: SKU TV-55 já cadastrado", domain.ErrConflict), http.StatusConflict, "SKU TV-55"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			api, f := newAdminAPI(t)
			f.products.err = tt.svcErr

			w := send(t, api, http.MethodPost, "/v1/admin/products", tt.body, adminHeaders(t), false)

			assert.Equal(t, tt.wantStatus, w.Code)
			assert.Contains(t, w.Body.String(), tt.wantInBody)
		})
	}
}

func TestAdmin_GetProduct(t *testing.T) {
	api, f := newAdminAPI(t)
	p := sampleProduct(true)
	p.Status = domain.ProductInactive // admin vê qualquer status
	f.products.product = p

	w := send(t, api, http.MethodGet, "/v1/admin/products/"+p.ID.String(), "", adminHeaders(t), true)

	require.Equal(t, http.StatusOK, w.Code)
	assert.Equal(t, p.ID, f.products.gotID)
	assert.Equal(t, `"3"`, w.Header().Get("ETag"))
	assert.Equal(t, "inactive", decode(t, w)["status"])
}

func TestAdmin_UpdateProduct(t *testing.T) {
	id := uuid.New()
	body := fmt.Sprintf(productBody, uuid.New())

	t.Run("If-Match vira a versão esperada", func(t *testing.T) {
		api, f := newAdminAPI(t)
		p := sampleProduct(false)
		p.Version = 4
		f.products.product = p

		w := send(t, api, http.MethodPut, "/v1/admin/products/"+id.String(), body, adminHeaders(t, "If-Match", `"3"`), true)

		require.Equal(t, http.StatusOK, w.Code)
		assert.Equal(t, id, f.products.gotID)
		assert.Equal(t, 3, f.products.gotVersion)
		assert.Equal(t, `"4"`, w.Header().Get("ETag"), "ETag da nova versão")
	})
	t.Run("ETag fraco também vale", func(t *testing.T) {
		api, f := newAdminAPI(t)
		f.products.product = sampleProduct(false)

		w := send(t, api, http.MethodPut, "/v1/admin/products/"+id.String(), body, adminHeaders(t, "If-Match", `W/"7"`), false)

		require.Equal(t, http.StatusOK, w.Code)
		assert.Equal(t, 7, f.products.gotVersion)
	})

	errs := []struct {
		name       string
		ifMatch    string
		svcErr     error
		wantStatus int
	}{
		{"sem If-Match", "", nil, http.StatusPreconditionRequired},
		{"If-Match que não é nosso", `"abc"`, nil, http.StatusPreconditionFailed},
		{"versão desatualizada", `"2"`, domain.ErrVersionConflict, http.StatusPreconditionFailed},
		{"produto inexistente", `"2"`, domain.ErrNotFound, http.StatusNotFound},
	}
	for _, tt := range errs {
		t.Run(tt.name, func(t *testing.T) {
			api, f := newAdminAPI(t)
			f.products.err = tt.svcErr
			headers := adminHeaders(t)
			if tt.ifMatch != "" {
				headers["If-Match"] = tt.ifMatch
			}

			w := send(t, api, http.MethodPut, "/v1/admin/products/"+id.String(), body, headers, false)

			assert.Equal(t, tt.wantStatus, w.Code)
			assert.Equal(t, "application/problem+json", w.Header().Get("Content-Type"))
		})
	}
}

func TestAdmin_DeleteProduct(t *testing.T) {
	t.Run("204", func(t *testing.T) {
		api, f := newAdminAPI(t)
		id := uuid.New()

		w := send(t, api, http.MethodDelete, "/v1/admin/products/"+id.String(), "", adminHeaders(t), true)

		assert.Equal(t, http.StatusNoContent, w.Code)
		assert.Equal(t, id, f.products.gotID)
		assert.Empty(t, w.Body.String())
	})
	t.Run("404", func(t *testing.T) {
		api, f := newAdminAPI(t)
		f.products.err = domain.ErrNotFound

		w := send(t, api, http.MethodDelete, "/v1/admin/products/"+uuid.NewString(), "", adminHeaders(t), true)

		assert.Equal(t, http.StatusNotFound, w.Code)
	})
	t.Run("id que não é UUID é 404 sem chamar o service", func(t *testing.T) {
		api, f := newAdminAPI(t)

		w := send(t, api, http.MethodDelete, "/v1/admin/products/123", "", adminHeaders(t), false)

		assert.Equal(t, http.StatusNotFound, w.Code)
		assert.Zero(t, f.products.deleteCalls)
	})
}

// ---------- categorias ----------

func TestAdmin_CreateCategory(t *testing.T) {
	api, f := newAdminAPI(t)
	parent := uuid.New()
	f.categories.cat = domain.Category{ID: uuid.New(), Slug: "tv", Name: "TVs", ParentID: &parent}

	w := send(t, api, http.MethodPost, "/v1/admin/categories",
		fmt.Sprintf(`{"slug":"tv","name":"TVs","parent_id":"%s"}`, parent), adminHeaders(t), true)

	require.Equal(t, http.StatusCreated, w.Code)
	assert.Equal(t, service.CategoryInput{Slug: "tv", Name: "TVs", ParentID: &parent}, f.categories.gotIn)
	assert.Equal(t, parent.String(), decode(t, w)["parent_id"])
}

// ---------- campanhas ----------

func sampleCampaign() domain.Campaign {
	start := time.Date(2026, 11, 27, 0, 0, 0, 0, time.UTC)
	return domain.Campaign{ID: uuid.New(), Slug: "bf", Name: "Black Friday", DiscountPercent: 30,
		StartsAt: start, EndsAt: start.Add(72 * time.Hour), ProductCount: 2}
}

const campaignBody = `{"slug":"bf","name":"Black Friday","discount_percent":30,
	"starts_at":"2026-11-27T00:00:00-03:00","ends_at":"2026-11-30T00:00:00-03:00"}`

func TestAdmin_ListCampaigns(t *testing.T) {
	api, f := newAdminAPI(t)
	f.campaigns.page = domain.Page[domain.Campaign]{Items: []domain.Campaign{sampleCampaign()}}

	w := send(t, api, http.MethodGet, "/v1/admin/campaigns?limit=5&cursor=abc", "", adminHeaders(t), true)

	require.Equal(t, http.StatusOK, w.Code)
	assert.Equal(t, "abc", f.campaigns.gotCursor)
	assert.Equal(t, 5, f.campaigns.gotLimit)
	assert.Equal(t, map[string]any{"next_cursor": nil, "has_more": false}, decode(t, w)["page"])
}

func TestAdmin_CreateCampaign(t *testing.T) {
	api, f := newAdminAPI(t)
	c := sampleCampaign()
	f.campaigns.camp = c

	w := send(t, api, http.MethodPost, "/v1/admin/campaigns", campaignBody, adminHeaders(t), true)

	require.Equal(t, http.StatusCreated, w.Code)
	assert.Equal(t, "/v1/admin/campaigns/"+c.ID.String(), w.Header().Get("Location"))
	assert.Equal(t, 30, f.campaigns.gotIn.DiscountPercent)
	assert.True(t, f.campaigns.gotIn.StartsAt.Equal(time.Date(2026, 11, 27, 3, 0, 0, 0, time.UTC)), "fuso preservado")
}

func TestAdmin_CreateCampaign_DataInvalida(t *testing.T) {
	api, _ := newAdminAPI(t)

	w := send(t, api, http.MethodPost, "/v1/admin/campaigns", `{"starts_at":"27/11/2026"}`, adminHeaders(t), false)

	assert.Equal(t, http.StatusBadRequest, w.Code)
	assert.Contains(t, w.Body.String(), "RFC 3339")
}

func TestAdmin_UpdateAndDeleteCampaign(t *testing.T) {
	id := uuid.New()

	t.Run("PUT 200", func(t *testing.T) {
		api, f := newAdminAPI(t)
		f.campaigns.camp = sampleCampaign()

		w := send(t, api, http.MethodPut, "/v1/admin/campaigns/"+id.String(), campaignBody, adminHeaders(t), true)

		require.Equal(t, http.StatusOK, w.Code)
		assert.Equal(t, id, f.campaigns.gotID)
	})
	t.Run("PUT 409 por sobreposição", func(t *testing.T) {
		api, f := newAdminAPI(t)
		f.campaigns.err = fmt.Errorf("%w: produtos já estão em outra campanha no período", domain.ErrConflict)

		w := send(t, api, http.MethodPut, "/v1/admin/campaigns/"+id.String(), campaignBody, adminHeaders(t), true)

		assert.Equal(t, http.StatusConflict, w.Code)
	})
	t.Run("DELETE 204", func(t *testing.T) {
		api, f := newAdminAPI(t)

		w := send(t, api, http.MethodDelete, "/v1/admin/campaigns/"+id.String(), "", adminHeaders(t), true)

		assert.Equal(t, http.StatusNoContent, w.Code)
		assert.Equal(t, id, f.campaigns.gotID)
	})
}

func TestAdmin_SetCampaignProducts(t *testing.T) {
	id := uuid.New()
	p1, p2 := uuid.New(), uuid.New()

	t.Run("204 com a lista", func(t *testing.T) {
		api, f := newAdminAPI(t)

		w := send(t, api, http.MethodPut, "/v1/admin/campaigns/"+id.String()+"/products",
			fmt.Sprintf(`{"product_ids":["%s","%s"]}`, p1, p2), adminHeaders(t), true)

		require.Equal(t, http.StatusNoContent, w.Code)
		assert.Equal(t, []uuid.UUID{p1, p2}, f.campaigns.gotIDs)
	})
	t.Run("lista vazia esvazia a campanha", func(t *testing.T) {
		api, f := newAdminAPI(t)

		w := send(t, api, http.MethodPut, "/v1/admin/campaigns/"+id.String()+"/products", `{"product_ids":[]}`, adminHeaders(t), true)

		require.Equal(t, http.StatusNoContent, w.Code)
		assert.NotNil(t, f.campaigns.gotIDs)
		assert.Empty(t, f.campaigns.gotIDs)
	})
	t.Run("sem product_ids é 422", func(t *testing.T) {
		api, f := newAdminAPI(t)

		w := send(t, api, http.MethodPut, "/v1/admin/campaigns/"+id.String()+"/products", `{}`, adminHeaders(t), false)

		assert.Equal(t, http.StatusUnprocessableEntity, w.Code)
		assert.Contains(t, w.Body.String(), `"field":"product_ids"`)
		assert.Equal(t, uuid.Nil, f.campaigns.gotID, "não chama o service")
	})
	t.Run("campanha inexistente é 404", func(t *testing.T) {
		api, f := newAdminAPI(t)
		f.campaigns.err = domain.ErrNotFound

		w := send(t, api, http.MethodPut, "/v1/admin/campaigns/"+id.String()+"/products",
			fmt.Sprintf(`{"product_ids":["%s"]}`, p1), adminHeaders(t), true)

		assert.Equal(t, http.StatusNotFound, w.Code)
	})
}
