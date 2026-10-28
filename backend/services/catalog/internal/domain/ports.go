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
	// Brands filtra por marca (qualquer uma delas, sem diferenciar maiúsculas).
	Brands []string
	// Sort é a ordem; vazio é relevância com Query e mais novos sem.
	Sort ProductSort
	// Now é o instante usado para decidir quais campanhas estão ativas.
	Now    time.Time
	Cursor string
	Limit  int
	// Page (a partir de 1) pagina por número, com o total; alternativa ao Cursor.
	Page int
}

// Page é uma página de resultados paginada por cursor opaco.
type Page[T any] struct {
	Items      []T
	NextCursor string // vazio quando não há próxima página
	// Total e More só na paginação por número de página: quantos itens a listagem tem e se há outra
	// página depois desta (sem cursor).
	Total *int
	More  bool
}

type ProductRepository interface {
	Create(ctx context.Context, p *Product) error
	// Update grava p se a versão atual for expectedVersion, e incrementa p.Version.
	Update(ctx context.Context, p *Product, expectedVersion int) error
	Delete(ctx context.Context, id uuid.UUID) error
	GetByID(ctx context.Context, id uuid.UUID, now time.Time) (Product, error)
	GetByIDs(ctx context.Context, ids []uuid.UUID, now time.Time) ([]Product, error)
	List(ctx context.Context, f ProductFilter) (Page[Product], error)
	// Brands conta as marcas dos produtos que passam no filtro (sem o filtro de marca), das mais
	// frequentes para as menos, até limit.
	Brands(ctx context.Context, f ProductFilter, limit int) ([]BrandCount, error)
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
	// ProductIDs devolve os produtos da campanha (qualquer status, sem os removidos).
	ProductIDs(ctx context.Context, campaignID uuid.UUID) ([]uuid.UUID, error)
	// SetProducts substitui a lista de produtos da campanha (transação única).
	SetProducts(ctx context.Context, campaignID uuid.UUID, productIDs []uuid.UUID) error
	// OverlappingProducts devolve, dentre productIDs, os que já estão em OUTRA campanha cujo período
	// cruza [start, end). Com productIDs nil, considera os produtos atuais da própria campanha.
	OverlappingProducts(ctx context.Context, campaignID uuid.UUID, start, end time.Time, productIDs []uuid.UUID) ([]uuid.UUID, error)
}

// ProductSort é a ordem da listagem pública (contrato: ProductSort).
type ProductSort string

const (
	SortRelevance ProductSort = "relevance"
	SortPriceAsc  ProductSort = "price_asc"
	SortPriceDesc ProductSort = "price_desc"
	SortDiscount  ProductSort = "discount"
	SortNewest    ProductSort = "newest"
)

// Valid diz se a ordem existe no contrato.
func (s ProductSort) Valid() bool {
	switch s {
	case SortRelevance, SortPriceAsc, SortPriceDesc, SortDiscount, SortNewest:
		return true
	}
	return false
}

// BrandCount é uma marca e quantos produtos da listagem a têm (a faceta do filtro de marca).
type BrandCount struct {
	Name  string
	Count int
}
