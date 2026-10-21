package repository

import (
	"time"

	"github.com/google/uuid"
	"gorm.io/gorm"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
)

// Registros GORM: espelham as tabelas e ficam restritos a este pacote.

type categoryRecord struct {
	ID        uuid.UUID `gorm:"type:uuid;primaryKey"`
	Slug      string
	Name      string
	ParentID  *uuid.UUID `gorm:"type:uuid"`
	CreatedAt time.Time
	UpdatedAt time.Time
}

func (categoryRecord) TableName() string { return "categories" }

type productRecord struct {
	ID          uuid.UUID `gorm:"type:uuid;primaryKey"`
	SKU         string    `gorm:"column:sku"`
	Name        string
	Description string
	CategoryID  uuid.UUID `gorm:"type:uuid"`
	ImageURL    *string
	PriceCents  int64
	Currency    string
	Status      string
	Version     int
	CreatedAt   time.Time
	UpdatedAt   time.Time
	DeletedAt   gorm.DeletedAt
}

func (productRecord) TableName() string { return "products" }

type campaignRecord struct {
	ID              uuid.UUID `gorm:"type:uuid;primaryKey"`
	Slug            string
	Name            string
	DiscountPercent int
	StartsAt        time.Time
	EndsAt          time.Time
	CreatedAt       time.Time
	UpdatedAt       time.Time
	// preenchido só nas leituras (subquery); nunca gravado
	ProductCount int `gorm:"->;-:migration"`
}

func (campaignRecord) TableName() string { return "campaigns" }

type campaignProductRecord struct {
	CampaignID uuid.UUID `gorm:"type:uuid;primaryKey"`
	ProductID  uuid.UUID `gorm:"type:uuid;primaryKey"`
}

func (campaignProductRecord) TableName() string { return "campaign_products" }

// productRow é o resultado da leitura de produto com categoria e campanha ativa.
// O GORM ignora campos anônimos de tipo não exportado, por isso o campo nomeado + embedded.
type productRow struct {
	Rec              productRecord `gorm:"embedded"`
	CategorySlug     string
	CategoryName     string
	CampaignID       *uuid.UUID
	CampaignName     *string
	CampaignDiscount *int
	CampaignEndsAt   *time.Time
	SearchScore      float64
}

func newID(id uuid.UUID) uuid.UUID {
	if id != uuid.Nil {
		return id
	}
	// v7 é ordenado no tempo: inserts sequenciais no índice da PK
	return uuid.Must(uuid.NewV7())
}

func (row productRow) toDomain() domain.Product {
	r := row.Rec
	p := domain.Product{
		ID:          r.ID,
		SKU:         r.SKU,
		Name:        r.Name,
		Description: r.Description,
		CategoryID:  r.CategoryID,
		Category:    domain.CategoryRef{ID: r.CategoryID, Slug: row.CategorySlug, Name: row.CategoryName},
		ImageURL:    r.ImageURL,
		PriceCents:  r.PriceCents,
		Currency:    r.Currency,
		Status:      domain.ProductStatus(r.Status),
		Version:     r.Version,
		CreatedAt:   r.CreatedAt,
		UpdatedAt:   r.UpdatedAt,
	}
	if row.CampaignID != nil {
		p.ActiveCampaign = &domain.CampaignRef{
			ID:              *row.CampaignID,
			Name:            deref(row.CampaignName),
			DiscountPercent: deref(row.CampaignDiscount),
			EndsAt:          deref(row.CampaignEndsAt),
		}
	}
	return p
}

func productToRecord(p *domain.Product) productRecord {
	currency := p.Currency
	if currency == "" {
		currency = domain.DefaultCurrency
	}
	return productRecord{
		ID:          p.ID,
		SKU:         p.SKU,
		Name:        p.Name,
		Description: p.Description,
		CategoryID:  p.CategoryID,
		ImageURL:    p.ImageURL,
		PriceCents:  p.PriceCents,
		Currency:    currency,
		Status:      string(p.Status),
		Version:     p.Version,
	}
}

func (r categoryRecord) toDomain() domain.Category {
	return domain.Category{
		ID: r.ID, Slug: r.Slug, Name: r.Name, ParentID: r.ParentID,
		CreatedAt: r.CreatedAt, UpdatedAt: r.UpdatedAt,
	}
}

func (r campaignRecord) toDomain() domain.Campaign {
	return domain.Campaign{
		ID: r.ID, Slug: r.Slug, Name: r.Name, DiscountPercent: r.DiscountPercent,
		StartsAt: r.StartsAt, EndsAt: r.EndsAt, ProductCount: r.ProductCount,
		CreatedAt: r.CreatedAt, UpdatedAt: r.UpdatedAt,
	}
}

func deref[T any](p *T) T {
	var zero T
	if p == nil {
		return zero
	}
	return *p
}
