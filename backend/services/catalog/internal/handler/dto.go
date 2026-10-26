package handler

import (
	"time"

	"github.com/google/uuid"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

// DTOs no formato de contracts/openapi/catalog.v1.yaml (JSON em snake_case, dinheiro em centavos).

type moneyDTO struct {
	AmountCents int64  `json:"amount_cents"`
	Currency    string `json:"currency"`
}

type categoryRefDTO struct {
	ID   uuid.UUID `json:"id"`
	Slug string    `json:"slug"`
	Name string    `json:"name"`
}

type campaignRefDTO struct {
	ID              uuid.UUID `json:"id"`
	Name            string    `json:"name"`
	DiscountPercent int       `json:"discount_percent"`
	EndsAt          time.Time `json:"ends_at"`
}

type productDTO struct {
	ID             uuid.UUID       `json:"id"`
	SKU            string          `json:"sku"`
	Name           string          `json:"name"`
	Description    string          `json:"description"`
	Category       categoryRefDTO  `json:"category"`
	ImageURL       *string         `json:"image_url"`
	Image          *imageDTO       `json:"image"`
	Price          moneyDTO        `json:"price"`
	EffectivePrice moneyDTO        `json:"effective_price"`
	Campaign       *campaignRefDTO `json:"campaign"`
	CreatedAt      time.Time       `json:"created_at"`
	UpdatedAt      time.Time       `json:"updated_at"`
}

// imageDTO é a ProductImage do contrato: a foto e as larguras para o srcset. Sem variantes conhecidas
// (URL definida pelo admin), só a url, sem dimensões e com variants vazio.
type imageDTO struct {
	URL      string                `json:"url"`
	Width    *int                  `json:"width"`
	Height   *int                  `json:"height"`
	Variants []domain.ImageVariant `json:"variants"`
}

func toImageDTO(p domain.Product) *imageDTO {
	if p.ImageURL == nil {
		return nil
	}
	dto := &imageDTO{URL: *p.ImageURL, Variants: []domain.ImageVariant{}}
	if img := p.Image; img != nil {
		dto.Width, dto.Height, dto.Variants = &img.Width, &img.Height, img.Variants
	}
	return dto
}

// adminProductDTO é o AdminProduct do contrato: Product + campos de gestão.
type adminProductDTO struct {
	productDTO
	Status  domain.ProductStatus `json:"status"`
	Version int                  `json:"version"`
}

type adminProductPageDTO struct {
	Data []adminProductDTO `json:"data"`
	Page pageInfoDTO       `json:"page"`
}

type pageInfoDTO struct {
	NextCursor *string `json:"next_cursor"`
	HasMore    bool    `json:"has_more"`
}

type productPageDTO struct {
	Data []productDTO `json:"data"`
	Page pageInfoDTO  `json:"page"`
}

type categoryDTO struct {
	ID       uuid.UUID  `json:"id"`
	Slug     string     `json:"slug"`
	Name     string     `json:"name"`
	ParentID *uuid.UUID `json:"parent_id"`
}

type campaignDTO struct {
	ID              uuid.UUID `json:"id"`
	Slug            string    `json:"slug"`
	Name            string    `json:"name"`
	DiscountPercent int       `json:"discount_percent"`
	StartsAt        time.Time `json:"starts_at"`
	EndsAt          time.Time `json:"ends_at"`
	ProductCount    int       `json:"product_count"`
}

type listDTO[T any] struct {
	Data []T `json:"data"`
}

func toProductDTO(p domain.Product) productDTO {
	dto := productDTO{
		ID:             p.ID,
		SKU:            p.SKU,
		Name:           p.Name,
		Description:    p.Description,
		Category:       categoryRefDTO{ID: p.Category.ID, Slug: p.Category.Slug, Name: p.Category.Name},
		ImageURL:       p.ImageURL,
		Image:          toImageDTO(p),
		Price:          moneyDTO{AmountCents: p.PriceCents, Currency: p.Currency},
		EffectivePrice: moneyDTO{AmountCents: p.EffectivePriceCents(), Currency: p.Currency},
		CreatedAt:      p.CreatedAt.UTC(),
		UpdatedAt:      p.UpdatedAt.UTC(),
	}
	if c := p.ActiveCampaign; c != nil {
		dto.Campaign = &campaignRefDTO{ID: c.ID, Name: c.Name, DiscountPercent: c.DiscountPercent, EndsAt: c.EndsAt.UTC()}
	}
	return dto
}

func toProductPageDTO(page domain.Page[domain.Product]) productPageDTO {
	out := productPageDTO{Data: make([]productDTO, len(page.Items))}
	for i, p := range page.Items {
		out.Data[i] = toProductDTO(p)
	}
	out.Page = toPageInfo(page.NextCursor)
	return out
}

func toAdminProductDTO(p domain.Product) adminProductDTO {
	return adminProductDTO{productDTO: toProductDTO(p), Status: p.Status, Version: p.Version}
}

func toAdminProductPageDTO(page domain.Page[domain.Product]) adminProductPageDTO {
	return adminProductPageDTO{Data: mapSlice(page.Items, toAdminProductDTO), Page: toPageInfo(page.NextCursor)}
}

func toPageInfo(nextCursor string) pageInfoDTO {
	if nextCursor == "" {
		return pageInfoDTO{}
	}
	return pageInfoDTO{NextCursor: &nextCursor, HasMore: true}
}

func toCategoryDTO(c domain.Category) categoryDTO {
	return categoryDTO{ID: c.ID, Slug: c.Slug, Name: c.Name, ParentID: c.ParentID}
}

func toCampaignDTO(c domain.Campaign) campaignDTO {
	return campaignDTO{
		ID: c.ID, Slug: c.Slug, Name: c.Name, DiscountPercent: c.DiscountPercent,
		StartsAt: c.StartsAt.UTC(), EndsAt: c.EndsAt.UTC(), ProductCount: c.ProductCount,
	}
}

func mapSlice[T, R any](in []T, f func(T) R) []R {
	out := make([]R, len(in))
	for i, v := range in {
		out[i] = f(v)
	}
	return out
}
