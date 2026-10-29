package handler

import (
	"context"
	"fmt"
	"log/slog"
	"net/http"
	"sort"
	"strconv"
	"strings"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/messaging"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/service"
)

type pricingAdmin interface {
	Settings(ctx context.Context) (service.PricingSettings, error)
	SetRule(ctx context.Context, rule domain.PricingRule) (domain.PricingRule, int, error)
	DeleteRule(ctx context.Context, categoryID uuid.UUID) (int, error)
}

// DeadLetterAdmin é a DLT do consumo do feed (messaging.DeadLetterStore).
type DeadLetterAdmin interface {
	Pending(ctx context.Context, limit int) ([]messaging.DeadLetter, error)
	Replay(ctx context.Context) (int, error)
}

// SupplierAdminHandler: as regras de margem do catálogo do fornecedor (#217) e a DLT do consumo do
// feed (ADR 0003), em /v1/admin/*.
type SupplierAdminHandler struct {
	pricing pricingAdmin
	// nil com o consumo do feed desligado: as rotas da DLT respondem 503
	deadLetters DeadLetterAdmin
	// invalida o cache da vitrine depois de reprecificar
	invalidate func(ctx context.Context)
	auth       gin.HandlerFunc
	log        *slog.Logger
}

func NewSupplierAdminHandler(pricing pricingAdmin, deadLetters DeadLetterAdmin, invalidate func(ctx context.Context),
	auth gin.HandlerFunc, log *slog.Logger,
) *SupplierAdminHandler {
	if invalidate == nil {
		invalidate = func(context.Context) {}
	}
	return &SupplierAdminHandler{pricing: pricing, deadLetters: deadLetters, invalidate: invalidate, auth: auth, log: log}
}

func (h *SupplierAdminHandler) Register(r gin.IRouter) {
	admin := r.Group("/v1/admin", h.auth)
	admin.GET("/pricing-rules", h.getPricing)
	admin.PUT("/pricing-rules/default", h.setDefaultRule)
	admin.PUT("/pricing-rules/categories/:category_id", h.setCategoryRule)
	admin.DELETE("/pricing-rules/categories/:category_id", h.deleteCategoryRule)
	admin.GET("/dead-letters", h.listDeadLetters)
	admin.POST("/dead-letters/replay", h.replayDeadLetters)
}

type pricingRuleDTO struct {
	CategoryID    *uuid.UUID `json:"category_id,omitempty"`
	MarginPercent int        `json:"margin_percent"`
	MinPriceCents int64      `json:"min_price_cents"`
	// nulo na padrão da configuração, que ainda não foi gravada no painel
	UpdatedAt *time.Time `json:"updated_at"`
}

type pricingSettingsDTO struct {
	Default       pricingRuleDTO    `json:"default"`
	Rules         []pricingRuleDTO  `json:"rules"`
	ExchangeRates map[string]string `json:"exchange_rates"`
}

type pricingRuleInputDTO struct {
	MarginPercent *int   `json:"margin_percent"`
	MinPriceCents *int64 `json:"min_price_cents"`
}

type pricingRuleResultDTO struct {
	Rule     pricingRuleDTO `json:"rule"`
	Repriced int            `json:"repriced"`
}

type repricedDTO struct {
	Repriced int `json:"repriced"`
}

func toPricingRuleDTO(r domain.PricingRule) pricingRuleDTO {
	dto := pricingRuleDTO{CategoryID: r.CategoryID, MarginPercent: r.MarginPercent, MinPriceCents: r.MinPriceCents}
	if !r.UpdatedAt.IsZero() {
		at := r.UpdatedAt
		dto.UpdatedAt = &at
	}
	return dto
}

// formatRate escreve o câmbio (milésimos) como o contrato: "5.40", "0.183".
func formatRate(milli int64) string {
	// uma casa a menos quando a terceira é zero: 5.400 → 5.40
	return strings.TrimSuffix(fmt.Sprintf("%d.%03d", milli/1000, milli%1000), "0")
}

func (h *SupplierAdminHandler) getPricing(c *gin.Context) {
	s, err := h.pricing.Settings(c.Request.Context())
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	rates := make(map[string]string, len(s.Rates))
	for currency, milli := range s.Rates {
		rates[currency] = formatRate(milli)
	}
	rules := mapSlice(s.Rules, toPricingRuleDTO)
	sort.Slice(rules, func(i, j int) bool { return rules[i].CategoryID.String() < rules[j].CategoryID.String() })
	c.JSON(http.StatusOK, pricingSettingsDTO{Default: toPricingRuleDTO(s.Default), Rules: rules, ExchangeRates: rates})
}

func (h *SupplierAdminHandler) setDefaultRule(c *gin.Context) {
	h.setRule(c, nil)
}

func (h *SupplierAdminHandler) setCategoryRule(c *gin.Context) {
	id, ok := pathUUID(c, "category_id")
	if !ok {
		return
	}
	h.setRule(c, &id)
}

func (h *SupplierAdminHandler) setRule(c *gin.Context, categoryID *uuid.UUID) {
	var in pricingRuleInputDTO
	if !bindJSON(c, &in) {
		return
	}
	var missing []domain.FieldError
	if in.MarginPercent == nil {
		missing = append(missing, domain.FieldError{Field: "margin_percent", Message: "obrigatório"})
	}
	if in.MinPriceCents == nil {
		missing = append(missing, domain.FieldError{Field: "min_price_cents", Message: "obrigatório"})
	}
	if len(missing) > 0 {
		writeValidationProblem(c, missing)
		return
	}
	rule, repriced, err := h.pricing.SetRule(c.Request.Context(),
		domain.PricingRule{CategoryID: categoryID, MarginPercent: *in.MarginPercent, MinPriceCents: *in.MinPriceCents})
	if repriced > 0 {
		h.invalidate(c.Request.Context()) // mesmo se parou no meio: o que mudou já está no banco
	}
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	h.audit(c, "regra de margem gravada", "category_id", categoryID, "margin_percent", rule.MarginPercent, "repriced", repriced)
	c.JSON(http.StatusOK, pricingRuleResultDTO{Rule: toPricingRuleDTO(rule), Repriced: repriced})
}

func (h *SupplierAdminHandler) deleteCategoryRule(c *gin.Context) {
	id, ok := pathUUID(c, "category_id")
	if !ok {
		return
	}
	repriced, err := h.pricing.DeleteRule(c.Request.Context(), id)
	if repriced > 0 {
		h.invalidate(c.Request.Context())
	}
	if err != nil {
		respondError(c, h.log, err)
		return
	}
	h.audit(c, "regra de margem removida", "category_id", id, "repriced", repriced)
	c.JSON(http.StatusOK, repricedDTO{Repriced: repriced})
}

// ---------- dead letter topic (contracts/openapi/common/v1/dead-letters.yaml) ----------

type deadLetterDTO struct {
	EventID   *uuid.UUID `json:"event_id"`
	EventType *string    `json:"event_type"`
	Topic     string     `json:"topic"`
	Partition *int       `json:"partition"`
	Offset    *int64     `json:"offset"`
	Attempts  int        `json:"attempts"`
	Error     *string    `json:"error"`
	FailedAt  *time.Time `json:"failed_at"`
}

func (h *SupplierAdminHandler) listDeadLetters(c *gin.Context) {
	limit := 50
	if raw := c.Query("limit"); raw != "" {
		n, err := strconv.Atoi(raw)
		if err != nil || n < 1 || n > 500 {
			writeValidationProblem(c, []domain.FieldError{{Field: "limit", Message: "deve estar entre 1 e 500"}})
			return
		}
		limit = n
	}
	if h.deadLetters == nil {
		kafkaUnavailable(c)
		return
	}
	items, err := h.deadLetters.Pending(c.Request.Context(), limit)
	if err != nil {
		h.log.WarnContext(c.Request.Context(), "DLT indisponível", "error", err.Error())
		kafkaUnavailable(c)
		return
	}
	c.JSON(http.StatusOK, gin.H{"items": mapSlice(items, func(d messaging.DeadLetter) deadLetterDTO {
		return deadLetterDTO{EventID: d.EventID, EventType: d.EventType, Topic: d.Topic, Partition: d.Partition,
			Offset: d.Offset, Attempts: d.Attempts, Error: d.Error, FailedAt: d.FailedAt}
	})})
}

func (h *SupplierAdminHandler) replayDeadLetters(c *gin.Context) {
	if h.deadLetters == nil {
		kafkaUnavailable(c)
		return
	}
	n, err := h.deadLetters.Replay(c.Request.Context())
	if err != nil {
		h.log.WarnContext(c.Request.Context(), "reprocessamento da DLT falhou", "error", err.Error(), "replayed", n)
		kafkaUnavailable(c)
		return
	}
	h.audit(c, "DLT reprocessada", "replayed", n)
	c.JSON(http.StatusOK, gin.H{"replayed": n})
}

func kafkaUnavailable(c *gin.Context) {
	c.Header("Retry-After", "30")
	WriteProblem(c, http.StatusServiceUnavailable, "Serviço indisponível", "o Kafka não respondeu; tente de novo")
}

func (h *SupplierAdminHandler) audit(c *gin.Context, msg string, args ...any) {
	args = append(args, ctxSubject, c.GetString(ctxSubject), ctxRequestID, c.GetString(ctxRequestID))
	h.log.InfoContext(c.Request.Context(), msg, args...)
}
