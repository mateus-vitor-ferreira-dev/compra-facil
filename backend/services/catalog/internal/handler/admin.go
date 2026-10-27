package handler

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/service"
)

// Interfaces mínimas dos services usadas pelas rotas admin.

type productAdmin interface {
	AdminList(ctx context.Context, in service.AdminListInput) (domain.Page[domain.Product], error)
	AdminGet(ctx context.Context, id uuid.UUID) (domain.Product, error)
	Create(ctx context.Context, in service.ProductInput) (domain.Product, error)
	Update(ctx context.Context, id uuid.UUID, in service.ProductInput, expectedVersion int) (domain.Product, error)
	Delete(ctx context.Context, id uuid.UUID) error
}

type categoryAdmin interface {
	Create(ctx context.Context, in service.CategoryInput) (domain.Category, error)
}

type campaignAdmin interface {
	AdminList(ctx context.Context, cursor string, limit int) (domain.Page[domain.Campaign], error)
	Create(ctx context.Context, in service.CampaignInput) (domain.Campaign, error)
	Update(ctx context.Context, id uuid.UUID, in service.CampaignInput) (domain.Campaign, error)
	Delete(ctx context.Context, id uuid.UUID) error
	SetProducts(ctx context.Context, id uuid.UUID, productIDs []uuid.UUID) error
}

// maxBodyBytes limita o corpo das escritas admin (a maior é a lista de 1000 produtos da campanha).
const maxBodyBytes = 1 << 20

// AdminHandler implementa as rotas /v1/admin/* de catalog.v1, todas atrás de auth.
type AdminHandler struct {
	products   productAdmin
	categories categoryAdmin
	campaigns  campaignAdmin
	auth       gin.HandlerFunc
	log        *slog.Logger
}

func NewAdminHandler(p productAdmin, c categoryAdmin, camp campaignAdmin, auth gin.HandlerFunc, log *slog.Logger) *AdminHandler {
	return &AdminHandler{products: p, categories: c, campaigns: camp, auth: auth, log: log}
}

func (h *AdminHandler) Register(r gin.IRouter) {
	admin := r.Group("/v1/admin", h.auth)
	admin.GET("/products", h.listProducts)
	admin.POST("/products", h.createProduct)
	admin.GET("/products/:product_id", h.getProduct)
	admin.PUT("/products/:product_id", h.updateProduct)
	admin.DELETE("/products/:product_id", h.deleteProduct)
	admin.POST("/categories", h.createCategory)
	admin.GET("/campaigns", h.listCampaigns)
	admin.POST("/campaigns", h.createCampaign)
	admin.PUT("/campaigns/:campaign_id", h.updateCampaign)
	admin.DELETE("/campaigns/:campaign_id", h.deleteCampaign)
	admin.PUT("/campaigns/:campaign_id/products", h.setCampaignProducts)
}

// ---------- produtos ----------

type productInputDTO struct {
	SKU         string               `json:"sku"`
	Name        string               `json:"name"`
	Description string               `json:"description"`
	CategoryID  uuid.UUID            `json:"category_id"`
	ImageURL    *string              `json:"image_url"`
	PriceCents  int64                `json:"price_cents"`
	Status      domain.ProductStatus `json:"status"`
}

func (in productInputDTO) toService() service.ProductInput {
	return service.ProductInput{
		SKU: in.SKU, Name: in.Name, Description: in.Description, CategoryID: in.CategoryID,
		ImageURL: in.ImageURL, PriceCents: in.PriceCents, Status: in.Status,
	}
}

func (h *AdminHandler) listProducts(c *gin.Context) {
	in := service.AdminListInput{SKU: c.Query("sku"), Cursor: c.Query("cursor")}
	if raw := c.Query("status"); raw != "" {
		status := domain.ProductStatus(raw)
		in.Status = &status
	}
	var err error
	if in.Limit, err = queryInt(c, "limit"); err != nil {
		badRequest(c, err.Error())
		return
	}
	page, err := h.products.AdminList(c.Request.Context(), in)
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	c.JSON(http.StatusOK, toAdminProductPageDTO(page))
}

func (h *AdminHandler) createProduct(c *gin.Context) {
	var in productInputDTO
	if !bindJSON(c, &in) {
		return
	}
	p, err := h.products.Create(c.Request.Context(), in.toService())
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	h.audit(c, "produto criado", "product_id", p.ID)
	c.Header("Location", "/v1/admin/products/"+p.ID.String())
	c.Header("ETag", etag(p.Version))
	c.JSON(http.StatusCreated, toAdminProductDTO(p))
}

func (h *AdminHandler) getProduct(c *gin.Context) {
	id, ok := pathUUID(c, "product_id")
	if !ok {
		return
	}
	p, err := h.products.AdminGet(c.Request.Context(), id)
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	c.Header("ETag", etag(p.Version))
	c.JSON(http.StatusOK, toAdminProductDTO(p))
}

func (h *AdminHandler) updateProduct(c *gin.Context) {
	id, ok := pathUUID(c, "product_id")
	if !ok {
		return
	}
	version, ok := ifMatchVersion(c)
	if !ok {
		return
	}
	var in productInputDTO
	if !bindJSON(c, &in) {
		return
	}
	p, err := h.products.Update(c.Request.Context(), id, in.toService(), version)
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	h.audit(c, "produto alterado", "product_id", p.ID)
	c.Header("ETag", etag(p.Version))
	c.JSON(http.StatusOK, toAdminProductDTO(p))
}

func (h *AdminHandler) deleteProduct(c *gin.Context) {
	id, ok := pathUUID(c, "product_id")
	if !ok {
		return
	}
	if err := h.products.Delete(c.Request.Context(), id); err != nil {
		respondError(c, h.log, err)
		return
	}
	h.audit(c, "produto removido", "product_id", id)
	c.Status(http.StatusNoContent)
}

// ---------- categorias ----------

type categoryInputDTO struct {
	Slug     string     `json:"slug"`
	Name     string     `json:"name"`
	ParentID *uuid.UUID `json:"parent_id"`
}

func (h *AdminHandler) createCategory(c *gin.Context) {
	var in categoryInputDTO
	if !bindJSON(c, &in) {
		return
	}
	cat, err := h.categories.Create(c.Request.Context(), service.CategoryInput{Slug: in.Slug, Name: in.Name, ParentID: in.ParentID})
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	h.audit(c, "categoria criada", "category_id", cat.ID)
	// não há GET por categoria: a listagem pública é o recurso que passa a contê-la
	c.Header("Location", "/v1/categories")
	c.JSON(http.StatusCreated, toCategoryDTO(cat))
}

// ---------- campanhas ----------

type campaignInputDTO struct {
	Slug            string    `json:"slug"`
	Name            string    `json:"name"`
	DiscountPercent int       `json:"discount_percent"`
	StartsAt        time.Time `json:"starts_at"`
	EndsAt          time.Time `json:"ends_at"`
}

func (in campaignInputDTO) toService() service.CampaignInput {
	return service.CampaignInput{
		Slug: in.Slug, Name: in.Name, DiscountPercent: in.DiscountPercent, StartsAt: in.StartsAt, EndsAt: in.EndsAt,
	}
}

type campaignPageDTO struct {
	Data []campaignDTO `json:"data"`
	Page pageInfoDTO   `json:"page"`
}

func (h *AdminHandler) listCampaigns(c *gin.Context) {
	limit, err := queryInt(c, "limit")
	if err != nil {
		badRequest(c, err.Error())
		return
	}
	page, err := h.campaigns.AdminList(c.Request.Context(), c.Query("cursor"), limit)
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	c.JSON(http.StatusOK, campaignPageDTO{Data: mapSlice(page.Items, toCampaignDTO), Page: toPageInfo(page.NextCursor)})
}

func (h *AdminHandler) createCampaign(c *gin.Context) {
	var in campaignInputDTO
	if !bindJSON(c, &in) {
		return
	}
	camp, err := h.campaigns.Create(c.Request.Context(), in.toService())
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	h.audit(c, "campanha criada", "campaign_id", camp.ID)
	c.Header("Location", "/v1/admin/campaigns/"+camp.ID.String())
	c.JSON(http.StatusCreated, toCampaignDTO(camp))
}

func (h *AdminHandler) updateCampaign(c *gin.Context) {
	id, ok := pathUUID(c, "campaign_id")
	if !ok {
		return
	}
	var in campaignInputDTO
	if !bindJSON(c, &in) {
		return
	}
	camp, err := h.campaigns.Update(c.Request.Context(), id, in.toService())
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	h.audit(c, "campanha alterada", "campaign_id", id)
	c.JSON(http.StatusOK, toCampaignDTO(camp))
}

func (h *AdminHandler) deleteCampaign(c *gin.Context) {
	id, ok := pathUUID(c, "campaign_id")
	if !ok {
		return
	}
	if err := h.campaigns.Delete(c.Request.Context(), id); err != nil {
		respondError(c, h.log, err)
		return
	}
	h.audit(c, "campanha removida", "campaign_id", id)
	c.Status(http.StatusNoContent)
}

type campaignProductsDTO struct {
	ProductIDs *[]uuid.UUID `json:"product_ids"`
}

func (h *AdminHandler) setCampaignProducts(c *gin.Context) {
	id, ok := pathUUID(c, "campaign_id")
	if !ok {
		return
	}
	var in campaignProductsDTO
	if !bindJSON(c, &in) {
		return
	}
	if in.ProductIDs == nil {
		// sem o campo não dá para distinguir "esvaziar" de "esqueci": o contrato o exige
		writeValidationProblem(c, []domain.FieldError{{Field: "product_ids", Message: "é obrigatório"}})
		return
	}
	if err := h.campaigns.SetProducts(c.Request.Context(), id, *in.ProductIDs); err != nil {
		respondError(c, h.log, err)
		return
	}
	h.audit(c, "produtos da campanha definidos", "campaign_id", id, "products", len(*in.ProductIDs))
	c.Status(http.StatusNoContent)
}

// ---------- apoio ----------

// audit registra quem alterou o quê (o subject vem do JWT).
func (h *AdminHandler) audit(c *gin.Context, msg string, args ...any) {
	args = append(args, ctxSubject, c.GetString(ctxSubject), ctxRequestID, c.GetString(ctxRequestID))
	h.log.InfoContext(c.Request.Context(), msg, args...)
}

// bindJSON decodifica o corpo recusando campos fora do contrato (additionalProperties: false).
// Responde 400 e devolve false se o JSON for inválido.
func bindJSON(c *gin.Context, dst any) bool {
	dec := json.NewDecoder(http.MaxBytesReader(c.Writer, c.Request.Body, maxBodyBytes))
	dec.DisallowUnknownFields()
	if err := dec.Decode(dst); err != nil {
		badRequest(c, jsonErrorDetail(err))
		return false
	}
	if dec.More() {
		badRequest(c, "o corpo deve ter um único objeto JSON")
		return false
	}
	return true
}

func jsonErrorDetail(err error) string {
	var (
		typeErr *json.UnmarshalTypeError
		syntax  *json.SyntaxError
		tooBig  *http.MaxBytesError
		timeErr *time.ParseError
	)
	switch {
	case errors.Is(err, io.EOF):
		return "corpo JSON obrigatório"
	case errors.As(err, &tooBig):
		return fmt.Sprintf("corpo maior que %d bytes", tooBig.Limit)
	case errors.As(err, &syntax), errors.Is(err, io.ErrUnexpectedEOF):
		return "JSON malformado"
	case errors.As(err, &typeErr):
		return fmt.Sprintf("campo %s com tipo errado", typeErr.Field)
	case errors.As(err, &timeErr):
		return "data em formato inválido (use RFC 3339, ex.: 2026-11-27T00:00:00Z)"
	case strings.HasPrefix(err.Error(), "json: unknown field "):
		return "campo desconhecido " + strings.TrimPrefix(err.Error(), "json: unknown field ")
	case strings.Contains(err.Error(), "invalid UUID"):
		return "UUID inválido"
	default:
		return "JSON inválido"
	}
}

// pathUUID lê um id do caminho; um valor que não é UUID não identifica nada (404).
func pathUUID(c *gin.Context, name string) (uuid.UUID, bool) {
	id, err := uuid.Parse(c.Param(name))
	if err != nil {
		WriteProblem(c, http.StatusNotFound, "Recurso não encontrado", "")
		return uuid.Nil, false
	}
	return id, true
}

// ifMatchVersion extrai a versão do If-Match (formato do etag()). Sem o header responde 428;
// um valor que não é um ETag nosso nunca casa com a versão atual, então responde 412.
func ifMatchVersion(c *gin.Context) (int, bool) {
	raw := strings.TrimSpace(c.GetHeader("If-Match"))
	if raw == "" {
		WriteProblem(c, http.StatusPreconditionRequired, "If-Match obrigatório",
			"envie If-Match com o ETag da última leitura do produto")
		return 0, false
	}
	version, err := strconv.Atoi(strings.Trim(strings.TrimPrefix(raw, "W/"), `"`))
	if err != nil || version < 1 {
		WriteProblem(c, http.StatusPreconditionFailed, "Versão desatualizada",
			"If-Match não corresponde a nenhuma versão do produto; leia de novo e reenvie com o ETag")
		return 0, false
	}
	return version, true
}
