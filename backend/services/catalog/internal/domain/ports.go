package domain

import (
	"context"
	"time"

	"github.com/google/uuid"
)

// ProductFilter descreve uma listagem de produtos. Campos zero não filtram.
type ProductFilter struct {
	Status      *ProductStatus
	CategoryIDs []uuid.UUID
	SKU         string
	// Query ativa a busca por texto e a ordenação por relevância.
	Query         string
	MinPriceCents *int64 // preço efetivo
	MaxPriceCents *int64 // preço efetivo
	OnSale        bool
	// Now é o instante usado para decidir quais campanhas estão ativas.
	Now    time.Time
	Cursor string
	Limit  int
}

// Page é uma página de resultados paginada por cursor opaco.
type Page[T any] struct {
	Items      []T
	NextCursor string // vazio quando não há próxima página
}

type ProductRepository interface {
	Create(ctx context.Context, p *Product) error
	// Update grava p se a versão atual for expectedVersion, e incrementa p.Version.
	Update(ctx context.Context, p *Product, expectedVersion int) error
	Delete(ctx context.Context, id uuid.UUID) error
	GetByID(ctx context.Context, id uuid.UUID, now time.Time) (Product, error)
	GetByIDs(ctx context.Context, ids []uuid.UUID, now time.Time) ([]Product, error)
	List(ctx context.Context, f ProductFilter) (Page[Product], error)
}

type CategoryRepository interface {
	Create(ctx context.Context, c *Category) error
	List(ctx context.Context) ([]Category, error)
	GetBySlug(ctx context.Context, slug string) (Category, error)
	// SubtreeIDs devolve o id da categoria e de todas as descendentes.
	SubtreeIDs(ctx context.Context, id uuid.UUID) ([]uuid.UUID, error)
}

type CampaignRepository interface {
	Create(ctx context.Context, c *Campaign) error
	Update(ctx context.Context, c *Campaign) error
	Delete(ctx context.Context, id uuid.UUID) error
	GetByID(ctx context.Context, id uuid.UUID) (Campaign, error)
	ListActive(ctx context.Context, now time.Time) ([]Campaign, error)
	// NextStart devolve o início da próxima campanha (a primeira com starts_at > now), de qualquer
	// produto; instante zero se não houver nenhuma agendada.
	NextStart(ctx context.Context, now time.Time) (time.Time, error)
	List(ctx context.Context, cursor string, limit int) (Page[Campaign], error)
	// SetProducts substitui a lista de produtos da campanha (transação única).
	SetProducts(ctx context.Context, campaignID uuid.UUID, productIDs []uuid.UUID) error
	// OverlappingProducts devolve, dentre productIDs, os que já estão em OUTRA campanha cujo período
	// cruza [start, end). Com productIDs nil, considera os produtos atuais da própria campanha.
	OverlappingProducts(ctx context.Context, campaignID uuid.UUID, start, end time.Time, productIDs []uuid.UUID) ([]uuid.UUID, error)
}
