package events

import (
	"context"
	"slices"
	"time"

	"github.com/google/uuid"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

// Tópicos do catálogo (contracts/asyncapi/catalog.v1.yaml).
const (
	TopicProducts  = "catalog.product.v1"
	TopicCampaigns = "catalog.campaign.v1"
)

// Tipos de evento (event_type).
const (
	TypeProductCreated      = "ProductCreated"
	TypeProductUpdated      = "ProductUpdated"
	TypeProductPriceChanged = "ProductPriceChanged"
	TypeProductDeleted      = "ProductDeleted"
	TypeCampaignCreated     = "CampaignCreated"
	TypeCampaignUpdated     = "CampaignUpdated"
	TypeCampaignDeleted     = "CampaignDeleted"
)

type money struct {
	AmountCents int64  `json:"amount_cents"`
	Currency    string `json:"currency"`
}

type productSnapshot struct {
	ProductID  uuid.UUID            `json:"product_id"`
	SKU        string               `json:"sku"`
	Name       string               `json:"name"`
	CategoryID uuid.UUID            `json:"category_id"`
	Price      money                `json:"price"`
	Status     domain.ProductStatus `json:"status"`
	Version    int                  `json:"version"`
	UpdatedAt  time.Time            `json:"updated_at"`
	// Supplier: a variante do fornecedor de dropshipping (nulo no produto da loja); o custo fica de fora
	Supplier *supplierRef `json:"supplier"`
}

type supplierRef struct {
	Name string `json:"name"`
	VID  string `json:"vid"`
}

func snapshotOf(p domain.Product) productSnapshot {
	var supplier *supplierRef
	if p.Supplier != nil {
		supplier = &supplierRef{Name: p.Supplier.Name, VID: p.Supplier.VID}
	}
	return productSnapshot{
		Supplier:   supplier,
		ProductID:  p.ID,
		SKU:        p.SKU,
		Name:       p.Name,
		CategoryID: p.CategoryID,
		Price:      money{AmountCents: p.PriceCents, Currency: p.Currency},
		Status:     p.Status,
		Version:    p.Version,
		UpdatedAt:  p.UpdatedAt.UTC(),
	}
}

// ProductCreated recebe o produto já gravado (id, versão e datas preenchidos).
func ProductCreated(ctx context.Context, p domain.Product, at time.Time) (Message, error) {
	return newMessage(ctx, TopicProducts, p.ID.String(), TypeProductCreated, at, snapshotOf(p))
}

// ProductUpdated recebe o produto no estado após a alteração.
func ProductUpdated(ctx context.Context, p domain.Product, at time.Time) (Message, error) {
	return newMessage(ctx, TopicProducts, p.ID.String(), TypeProductUpdated, at, snapshotOf(p))
}

// ProductPriceChanged acompanha o ProductUpdated da mesma alteração quando o preço de tabela muda.
func ProductPriceChanged(ctx context.Context, p domain.Product, oldCents int64, oldCurrency string, at time.Time) (Message, error) {
	return newMessage(ctx, TopicProducts, p.ID.String(), TypeProductPriceChanged, at, struct {
		ProductID uuid.UUID `json:"product_id"`
		SKU       string    `json:"sku"`
		OldPrice  money     `json:"old_price"`
		NewPrice  money     `json:"new_price"`
		Version   int       `json:"version"`
	}{p.ID, p.SKU, money{oldCents, oldCurrency}, money{p.PriceCents, p.Currency}, p.Version})
}

// ProductDeleted leva a versão do produto após a remoção (a remoção também sobe a versão).
func ProductDeleted(ctx context.Context, id uuid.UUID, sku string, version int, at time.Time) (Message, error) {
	return newMessage(ctx, TopicProducts, id.String(), TypeProductDeleted, at, struct {
		ProductID uuid.UUID `json:"product_id"`
		SKU       string    `json:"sku"`
		Version   int       `json:"version"`
	}{id, sku, version})
}

type campaignSnapshot struct {
	CampaignID      uuid.UUID   `json:"campaign_id"`
	Slug            string      `json:"slug"`
	Name            string      `json:"name"`
	DiscountPercent int         `json:"discount_percent"`
	StartsAt        time.Time   `json:"starts_at"`
	EndsAt          time.Time   `json:"ends_at"`
	ProductIDs      []uuid.UUID `json:"product_ids"`
	Version         int         `json:"version"`
}

func campaignSnapshotOf(c domain.Campaign, productIDs []uuid.UUID) campaignSnapshot {
	ids := slices.Clone(productIDs)
	if ids == nil {
		ids = []uuid.UUID{} // o contrato exige a lista, mesmo vazia
	}
	slices.SortFunc(ids, func(a, b uuid.UUID) int { return slices.Compare(a[:], b[:]) })
	return campaignSnapshot{
		CampaignID:      c.ID,
		Slug:            c.Slug,
		Name:            c.Name,
		DiscountPercent: c.DiscountPercent,
		StartsAt:        c.StartsAt.UTC(),
		EndsAt:          c.EndsAt.UTC(),
		ProductIDs:      ids,
		Version:         c.Version,
	}
}

// CampaignCreated recebe a campanha já gravada. Pela API ela nasce sem produtos; o seed já cria com eles.
func CampaignCreated(ctx context.Context, c domain.Campaign, productIDs []uuid.UUID, at time.Time) (Message, error) {
	return newMessage(ctx, TopicCampaigns, c.ID.String(), TypeCampaignCreated, at, campaignSnapshotOf(c, productIDs))
}

// CampaignUpdated leva o estado completo após qualquer alteração, inclusive a lista de produtos.
func CampaignUpdated(ctx context.Context, c domain.Campaign, productIDs []uuid.UUID, at time.Time) (Message, error) {
	return newMessage(ctx, TopicCampaigns, c.ID.String(), TypeCampaignUpdated, at, campaignSnapshotOf(c, productIDs))
}

// CampaignDeleted leva a versão após a remoção.
func CampaignDeleted(ctx context.Context, id uuid.UUID, version int, at time.Time) (Message, error) {
	return newMessage(ctx, TopicCampaigns, id.String(), TypeCampaignDeleted, at, struct {
		CampaignID uuid.UUID `json:"campaign_id"`
		Version    int       `json:"version"`
	}{id, version})
}
