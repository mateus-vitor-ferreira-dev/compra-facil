package handler

import (
	"context"
	"fmt"
	"log/slog"
	"net/http"
	"strconv"
	"strings"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/service"
)

// Interfaces mínimas dos services usadas pelas rotas públicas (facilitam os testes do handler).

type productReader interface {
	ListPublic(ctx context.Context, in service.ListProductsInput) (domain.Page[domain.Product], error)
	ListBrands(ctx context.Context, in service.ListProductsInput) ([]domain.BrandCount, error)
	ListCategoryCounts(ctx context.Context, in service.ListProductsInput) ([]domain.CategoryCount, error)
	GetPublic(ctx context.Context, id uuid.UUID) (domain.Product, error)
}

type categoryReader interface {
	List(ctx context.Context) ([]domain.Category, error)
}

type campaignReader interface {
	ListActive(ctx context.Context) ([]domain.Campaign, error)
}

// CatalogHandler implementa as rotas públicas de catalog.v1.
type CatalogHandler struct {
	products   productReader
	categories categoryReader
	campaigns  campaignReader
	log        *slog.Logger
}

func NewCatalogHandler(p productReader, c categoryReader, camp campaignReader, log *slog.Logger) *CatalogHandler {
	return &CatalogHandler{products: p, categories: c, campaigns: camp, log: log}
}

func (h *CatalogHandler) Register(r gin.IRouter) {
	v1 := r.Group("/v1")
	v1.GET("/products", h.listProducts)
	v1.GET("/products/:product_id", h.getProduct)
	v1.GET("/brands", h.listBrands)
	v1.GET("/category-counts", h.listCategoryCounts)
	v1.GET("/categories", h.listCategories)
	v1.GET("/campaigns", h.listActiveCampaigns)
}

func (h *CatalogHandler) listProducts(c *gin.Context) {
	in, ok := listInput(c)
	if !ok {
		return
	}
	page, err := h.products.ListPublic(c.Request.Context(), in)
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	c.JSON(http.StatusOK, toProductPageDTO(page))
}

func (h *CatalogHandler) listBrands(c *gin.Context) {
	in, ok := listInput(c)
	if !ok {
		return
	}
	brands, err := h.products.ListBrands(c.Request.Context(), in)
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	out := brandsDTO{Data: make([]brandCountDTO, len(brands))}
	for i, b := range brands {
		out.Data[i] = brandCountDTO(b)
	}
	c.JSON(http.StatusOK, out)
}

func (h *CatalogHandler) listCategoryCounts(c *gin.Context) {
	in, ok := listInput(c)
	if !ok {
		return
	}
	counts, err := h.products.ListCategoryCounts(c.Request.Context(), in)
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	out := categoryCountsDTO{Data: make([]categoryCountDTO, len(counts))}
	for i, cc := range counts {
		out.Data[i] = categoryCountDTO(cc)
	}
	c.JSON(http.StatusOK, out)
}

// listInput lê os filtros da listagem (os mesmos de /v1/brands e /v1/category-counts); responde 400 e devolve false se algum
// não tem o tipo certo.
func listInput(c *gin.Context) (service.ListProductsInput, bool) {
	in := service.ListProductsInput{
		Query:        c.Query("q"),
		CategorySlug: c.Query("category"),
		Cursor:       c.Query("cursor"),
		Sort:         domain.ProductSort(c.Query("sort")),
	}
	if raw := c.Query("brand"); raw != "" {
		in.Brands = strings.Split(raw, ",")
	}
	if raw := c.Query("type"); raw != "" {
		in.Types = strings.Split(raw, ",")
	}
	var err error
	if in.Page, err = queryInt(c, "page"); err != nil {
		badRequest(c, err.Error())
		return in, false
	}
	if in.Limit, err = queryInt(c, "limit"); err == nil {
		if in.MinPriceCents, err = queryInt64Ptr(c, "min_price_cents"); err == nil {
			if in.MaxPriceCents, err = queryInt64Ptr(c, "max_price_cents"); err == nil {
				in.OnSale, err = queryBool(c, "on_sale")
			}
		}
	}
	if err != nil {
		badRequest(c, err.Error())
		return in, false
	}
	return in, true
}

func (h *CatalogHandler) getProduct(c *gin.Context) {
	id, err := uuid.Parse(c.Param("product_id"))
	if err != nil {
		// um id que não é UUID não identifica nenhum produto
		WriteProblem(c, http.StatusNotFound, "Recurso não encontrado", "")
		return
	}
	p, err := h.products.GetPublic(c.Request.Context(), id)
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	c.Header("ETag", etag(p.Version))
	c.JSON(http.StatusOK, toProductDTO(p))
}

func (h *CatalogHandler) listCategories(c *gin.Context) {
	cats, err := h.categories.List(c.Request.Context())
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	c.JSON(http.StatusOK, listDTO[categoryDTO]{Data: mapSlice(cats, toCategoryDTO)})
}

func (h *CatalogHandler) listActiveCampaigns(c *gin.Context) {
	cs, err := h.campaigns.ListActive(c.Request.Context())
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	c.JSON(http.StatusOK, listDTO[campaignDTO]{Data: mapSlice(cs, toCampaignDTO)})
}

// etag usa a versão do produto (incrementada a cada alteração); o If-Match do PUT admin compara com ela.
func etag(version int) string { return fmt.Sprintf(`"%d"`, version) }

func queryInt(c *gin.Context, name string) (int, error) {
	raw, ok := c.GetQuery(name)
	if !ok || raw == "" {
		return 0, nil
	}
	v, err := strconv.Atoi(raw)
	if err != nil {
		return 0, fmt.Errorf("%s deve ser um número inteiro", name)
	}
	return v, nil
}

func queryInt64Ptr(c *gin.Context, name string) (*int64, error) {
	raw, ok := c.GetQuery(name)
	if !ok || raw == "" {
		return nil, nil //nolint:nilnil // ausente é um valor válido
	}
	v, err := strconv.ParseInt(raw, 10, 64)
	if err != nil {
		return nil, fmt.Errorf("%s deve ser um número inteiro", name)
	}
	return &v, nil
}

func queryBool(c *gin.Context, name string) (bool, error) {
	raw, ok := c.GetQuery(name)
	if !ok || raw == "" {
		return false, nil
	}
	v, err := strconv.ParseBool(raw)
	if err != nil {
		return false, fmt.Errorf("%s deve ser true ou false", name)
	}
	return v, nil
}
