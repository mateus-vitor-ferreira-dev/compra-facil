package handler

import (
	"context"
	"errors"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/pb33f/libopenapi"
	validator "github.com/pb33f/libopenapi-validator"
	"github.com/pb33f/libopenapi/datamodel"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/api/openapi"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/service"
)

// ---------- fakes dos services ----------

type fakeProducts struct {
	page    domain.Page[domain.Product]
	brands  []domain.BrandCount
	counts  []domain.CategoryCount
	product domain.Product
	err     error
	gotIn   service.ListProductsInput
}

func (f *fakeProducts) ListPublic(_ context.Context, in service.ListProductsInput) (domain.Page[domain.Product], error) {
	f.gotIn = in
	return f.page, f.err
}

func (f *fakeProducts) ListBrands(_ context.Context, in service.ListProductsInput) ([]domain.BrandCount, error) {
	f.gotIn = in
	return f.brands, f.err
}

func (f *fakeProducts) ListCategoryCounts(_ context.Context, in service.ListProductsInput) ([]domain.CategoryCount, error) {
	f.gotIn = in
	return f.counts, f.err
}

func (f *fakeProducts) GetPublic(context.Context, uuid.UUID) (domain.Product, error) {
	return f.product, f.err
}

type fakeCategories struct {
	cats []domain.Category
	err  error
}

func (f fakeCategories) List(context.Context) ([]domain.Category, error) { return f.cats, f.err }

type fakeCampaigns struct {
	cs  []domain.Campaign
	err error
}

func (f fakeCampaigns) ListActive(context.Context) ([]domain.Campaign, error) { return f.cs, f.err }

// ---------- validação contra o contrato ----------

// contract valida request e response contra catalog.v1.yaml (OpenAPI 3.1, com o $ref para common).
func contract(t *testing.T) validator.Validator {
	t.Helper()
	spec, err := openapi.Files.ReadFile(openapi.Spec)
	require.NoError(t, err)
	doc, err := libopenapi.NewDocumentWithConfiguration(spec, &datamodel.DocumentConfiguration{
		BasePath:            "../../api/openapi",
		AllowFileReferences: true,
	})
	require.NoError(t, err)
	v, errs := validator.NewValidator(doc)
	require.Empty(t, errs)
	return v
}

func serve(t *testing.T, h http.Handler, path string) *httptest.ResponseRecorder {
	t.Helper()
	req := httptest.NewRequestWithContext(context.Background(), http.MethodGet, "http://localhost:8001"+path, nil)
	w := httptest.NewRecorder()
	h.ServeHTTP(w, req)

	ok, errs := contract(t).ValidateHttpRequestResponse(req, w.Result())
	if !ok {
		for _, e := range errs {
			t.Errorf("fora do contrato: %s — %s", e.Message, e.Reason)
			for _, se := range e.SchemaValidationErrors {
				t.Errorf("  %s: %s", se.FieldPath, se.Reason)
			}
		}
		t.Logf("corpo: %s", w.Body.String())
	}
	return w
}

func sampleProduct(withCampaign bool) domain.Product {
	img := "https://cdn.example.com/tv.jpg"
	brand := "Samsung"
	created := time.Date(2026, 10, 1, 10, 0, 0, 0, time.FixedZone("BRT", -3*3600))
	p := domain.Product{
		ID: uuid.New(), SKU: "TV-55", Name: "Smart TV 55", Description: "QLED", Brand: &brand, CategoryID: uuid.New(),
		Category: domain.CategoryRef{ID: uuid.New(), Slug: "tv", Name: "TVs"}, ImageURL: &img,
		PriceCents: 10000, Currency: "BRL", Status: domain.ProductActive, Version: 3,
		CreatedAt: created, UpdatedAt: created,
	}
	if withCampaign {
		p.ActiveCampaign = &domain.CampaignRef{ID: uuid.New(), Name: "BF", DiscountPercent: 30, EndsAt: created.Add(time.Hour)}
	}
	return p
}

func newAPI(p *fakeProducts, c fakeCategories, camp fakeCampaigns) http.Handler {
	return NewRouter(discardLogger(), NewHealth(), NewCatalogHandler(p, c, camp, discardLogger()))
}

// ---------- testes ----------

func TestListProducts(t *testing.T) {
	products := &fakeProducts{page: domain.Page[domain.Product]{
		Items: []domain.Product{sampleProduct(true), sampleProduct(false)}, NextCursor: "abc",
	}}
	api := newAPI(products, fakeCategories{}, fakeCampaigns{})

	w := serve(t, api, "/v1/products?q=tv&category=tv&min_price_cents=100&max_price_cents=900&on_sale=true&limit=2&cursor=xyz")

	require.Equal(t, http.StatusOK, w.Code)
	assert.Equal(t, service.ListProductsInput{
		Query: "tv", CategorySlug: "tv", MinPriceCents: ptr(int64(100)), MaxPriceCents: ptr(int64(900)),
		OnSale: true, Limit: 2, Cursor: "xyz",
	}, products.gotIn)

	body := decode(t, w)
	data := body["data"].([]any)
	require.Len(t, data, 2)
	first := data[0].(map[string]any)
	assert.InDelta(t, 7000, first["effective_price"].(map[string]any)["amount_cents"], 0, "com 30% de desconto")
	assert.Equal(t, "2026-10-01T13:00:00Z", first["created_at"], "datas em UTC")
	assert.Equal(t, "Samsung", first["brand"])
	assert.Nil(t, data[1].(map[string]any)["campaign"], "sem campanha é null")
	assert.Equal(t, map[string]any{"next_cursor": "abc", "has_more": true}, body["page"])
}

func TestListProducts_SortAndBrands(t *testing.T) {
	products := &fakeProducts{page: domain.Page[domain.Product]{Items: []domain.Product{}}}
	api := newAPI(products, fakeCategories{}, fakeCampaigns{})

	w := serve(t, api, "/v1/products?sort=price_asc&brand=Dell,Acer")

	require.Equal(t, http.StatusOK, w.Code)
	assert.Equal(t, domain.SortPriceAsc, products.gotIn.Sort)
	assert.Equal(t, []string{"Dell", "Acer"}, products.gotIn.Brands)
}

func TestListProducts_NumberedPage_HasTheTotal(t *testing.T) {
	total := 1234
	products := &fakeProducts{page: domain.Page[domain.Product]{
		Items: []domain.Product{sampleProduct(false)}, Total: &total, More: true,
	}}
	api := newAPI(products, fakeCategories{}, fakeCampaigns{})

	w := serve(t, api, "/v1/products?page=3&limit=24")

	require.Equal(t, http.StatusOK, w.Code)
	assert.Equal(t, 3, products.gotIn.Page)
	body := decode(t, w)
	assert.InDelta(t, 1234, body["total"], 0)
	assert.Equal(t, map[string]any{"next_cursor": nil, "has_more": true}, body["page"])
}

func TestListBrands(t *testing.T) {
	products := &fakeProducts{brands: []domain.BrandCount{{Name: "Dell", Count: 3}, {Name: "Acer", Count: 1}}}
	api := newAPI(products, fakeCategories{}, fakeCampaigns{})

	w := serve(t, api, "/v1/brands?category=notebooks&on_sale=true")

	require.Equal(t, http.StatusOK, w.Code)
	assert.JSONEq(t, `{"data":[{"name":"Dell","count":3},{"name":"Acer","count":1}]}`, w.Body.String())
	assert.Equal(t, service.ListProductsInput{CategorySlug: "notebooks", OnSale: true}, products.gotIn)
}

// a faceta de tipos (#212): as filhas com a contagem, e o tipo chega ao serviço na listagem
func TestListCategoryCounts(t *testing.T) {
	products := &fakeProducts{counts: []domain.CategoryCount{{Slug: "mouses", Name: "Mouses", Count: 7}}}
	api := newAPI(products, fakeCategories{}, fakeCampaigns{})

	w := serve(t, api, "/v1/category-counts?category=perifericos&brand=Logitech")

	require.Equal(t, http.StatusOK, w.Code)
	assert.JSONEq(t, `{"data":[{"slug":"mouses","name":"Mouses","count":7}]}`, w.Body.String())
	assert.Equal(t, service.ListProductsInput{CategorySlug: "perifericos", Brands: []string{"Logitech"}}, products.gotIn)
}

func TestListProducts_Types(t *testing.T) {
	products := &fakeProducts{page: domain.Page[domain.Product]{Items: []domain.Product{}}}
	api := newAPI(products, fakeCategories{}, fakeCampaigns{})

	w := serve(t, api, "/v1/products?category=perifericos&type=mouses,teclados")

	require.Equal(t, http.StatusOK, w.Code)
	assert.Equal(t, []string{"mouses", "teclados"}, products.gotIn.Types)
}

func TestListProducts_EmptyAndLastPage(t *testing.T) {
	api := newAPI(&fakeProducts{page: domain.Page[domain.Product]{Items: []domain.Product{}}}, fakeCategories{}, fakeCampaigns{})

	w := serve(t, api, "/v1/products")

	require.Equal(t, http.StatusOK, w.Code)
	assert.JSONEq(t, `{"data":[],"page":{"next_cursor":null,"has_more":false}}`, w.Body.String())
}

func TestListProducts_Errors(t *testing.T) {
	tests := []struct {
		name       string
		query      string
		svcErr     error
		wantStatus int
		wantInBody string
	}{
		{"limit não numérico", "?limit=dez", nil, http.StatusBadRequest, "limit deve ser um número inteiro"},
		{"preço não numérico", "?min_price_cents=1,5", nil, http.StatusBadRequest, "min_price_cents"},
		{"on_sale inválido", "?on_sale=talvez", nil, http.StatusBadRequest, "on_sale"},
		{
			"validação do service vira 422 com os campos", "?q=tv",
			&domain.ValidationError{Fields: []domain.FieldError{{Field: "q", Message: "curta"}, {Field: "limit", Message: "grande"}}},
			http.StatusUnprocessableEntity, `"errors":[{"field":"q","message":"curta"},{"field":"limit","message":"grande"}]`,
		},
		{"erro inesperado vira 500 sem vazar detalhe", "", errors.New("senha do banco: hunter2"), http.StatusInternalServerError, "Erro interno"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			api := newAPI(&fakeProducts{err: tt.svcErr}, fakeCategories{}, fakeCampaigns{})

			// o validador recusaria a própria requisição (tipo errado); aqui interessa só a resposta
			w := do(t, api, http.MethodGet, "/v1/products"+tt.query, nil)

			assert.Equal(t, tt.wantStatus, w.Code)
			assert.Equal(t, "application/problem+json", w.Header().Get("Content-Type"))
			assert.Contains(t, w.Body.String(), tt.wantInBody)
			assert.NotContains(t, w.Body.String(), "hunter2")
		})
	}
	t.Run("422 aderente ao contrato", func(t *testing.T) {
		ve := &domain.ValidationError{Fields: []domain.FieldError{{Field: "q", Message: "curta"}}}
		w := serve(t, newAPI(&fakeProducts{err: ve}, fakeCategories{}, fakeCampaigns{}), "/v1/products?q=tv")
		assert.Equal(t, http.StatusUnprocessableEntity, w.Code)
	})
}

func TestGetProduct(t *testing.T) {
	p := sampleProduct(true)

	t.Run("200 com ETag", func(t *testing.T) {
		w := serve(t, newAPI(&fakeProducts{product: p}, fakeCategories{}, fakeCampaigns{}), "/v1/products/"+p.ID.String())

		require.Equal(t, http.StatusOK, w.Code)
		assert.Equal(t, `"3"`, w.Header().Get("ETag"))
		assert.Equal(t, p.ID.String(), decode(t, w)["id"])
	})
	t.Run("404 do service", func(t *testing.T) {
		w := serve(t, newAPI(&fakeProducts{err: domain.ErrNotFound}, fakeCategories{}, fakeCampaigns{}), "/v1/products/"+uuid.NewString())
		assert.Equal(t, http.StatusNotFound, w.Code)
	})
	t.Run("id que não é UUID é 404", func(t *testing.T) {
		w := do(t, newAPI(&fakeProducts{}, fakeCategories{}, fakeCampaigns{}), http.MethodGet, "/v1/products/123", nil)
		assert.Equal(t, http.StatusNotFound, w.Code)
	})
}

func TestListCategoriesAndCampaigns(t *testing.T) {
	parent := uuid.New()
	start := time.Date(2026, 11, 27, 0, 0, 0, 0, time.UTC)
	api := newAPI(&fakeProducts{},
		fakeCategories{cats: []domain.Category{{ID: parent, Slug: "eletro", Name: "Eletro"}, {ID: uuid.New(), Slug: "tv", Name: "TV", ParentID: &parent}}},
		fakeCampaigns{cs: []domain.Campaign{{ID: uuid.New(), Slug: "bf", Name: "Black Friday", DiscountPercent: 30, StartsAt: start, EndsAt: start.Add(24 * time.Hour), ProductCount: 12}}},
	)

	cats := serve(t, api, "/v1/categories")
	require.Equal(t, http.StatusOK, cats.Code)
	data := decode(t, cats)["data"].([]any)
	require.Len(t, data, 2)
	assert.Nil(t, data[0].(map[string]any)["parent_id"])
	assert.Equal(t, parent.String(), data[1].(map[string]any)["parent_id"])

	camps := serve(t, api, "/v1/campaigns")
	require.Equal(t, http.StatusOK, camps.Code)
	assert.Contains(t, camps.Body.String(), `"product_count":12`)
}

func TestListCategoriesAndCampaigns_Errors(t *testing.T) {
	boom := errors.New("boom")
	api := newAPI(&fakeProducts{}, fakeCategories{err: boom}, fakeCampaigns{err: boom})

	for _, path := range []string{"/v1/categories", "/v1/campaigns"} {
		w := serve(t, api, path)
		assert.Equal(t, http.StatusInternalServerError, w.Code, path)
	}
}

func TestDocs(t *testing.T) {
	api := NewRouter(discardLogger(), NewHealth(), NewDocs(openapi.Files))

	// /docs e /openapi não fazem parte do contrato: requisição direta, sem o validador
	page := do(t, api, http.MethodGet, "/docs", nil)
	assert.Equal(t, http.StatusOK, page.Code)
	assert.Contains(t, page.Body.String(), `url: "openapi/catalog.v1.yaml"`)

	for _, path := range []string{"/openapi/catalog.v1.yaml", "/openapi/common/v1/common.yaml"} {
		w := do(t, api, http.MethodGet, path, nil)
		require.Equal(t, http.StatusOK, w.Code, path)
		assert.Contains(t, w.Body.String(), "openapi: 3.1.0", path)
	}
}

func ptr[T any](v T) *T { return &v }

// Garante que o validador de contrato de fato reprova respostas fora do schema.
func TestContractValidatorCatchesDrift(t *testing.T) {
	bad := gin.New()
	bad.GET("/v1/categories", func(c *gin.Context) {
		c.JSON(http.StatusOK, gin.H{"data": []gin.H{{"id": "nao-e-uuid", "name": 1}}}) // sem slug, tipos errados
	})
	req := httptest.NewRequestWithContext(context.Background(), http.MethodGet, "http://localhost:8001/v1/categories", nil)
	w := httptest.NewRecorder()
	bad.ServeHTTP(w, req)

	ok, errs := contract(t).ValidateHttpResponse(req, w.Result())

	assert.False(t, ok)
	assert.NotEmpty(t, errs)
}

func TestGetProduct_Image(t *testing.T) {
	get := func(t *testing.T, p domain.Product) map[string]any {
		t.Helper()
		w := serve(t, newAPI(&fakeProducts{product: p}, fakeCategories{}, fakeCampaigns{}), "/v1/products/"+p.ID.String())
		require.Equal(t, http.StatusOK, w.Code)
		return decode(t, w)
	}

	t.Run("com variantes: o srcset do front", func(t *testing.T) {
		p := sampleProduct(false)
		big := "http://localhost/media/produtos/tv-1.d226c7dd-683.webp"
		p.ImageURL = &big
		p.Image = &domain.ProductImage{Width: 683, Height: 683, Variants: []domain.ImageVariant{
			{Width: 320, URL: "http://localhost/media/produtos/tv-1.d226c7dd-320.webp"},
			{Width: 683, URL: big},
		}}

		img := get(t, p)["image"].(map[string]any)

		assert.Equal(t, big, img["url"])
		assert.InDelta(t, 683, img["width"], 0)
		variants := img["variants"].([]any)
		require.Len(t, variants, 2)
		assert.InDelta(t, 320, variants[0].(map[string]any)["width"], 0)
	})
	t.Run("só a URL (definida pelo admin): sem dimensões nem variantes", func(t *testing.T) {
		img := get(t, sampleProduct(false))["image"].(map[string]any)

		assert.Equal(t, "https://cdn.example.com/tv.jpg", img["url"])
		assert.Nil(t, img["width"])
		assert.Empty(t, img["variants"])
	})
	t.Run("sem foto", func(t *testing.T) {
		p := sampleProduct(false)
		p.ImageURL = nil
		assert.Nil(t, get(t, p)["image"])
	})
}
