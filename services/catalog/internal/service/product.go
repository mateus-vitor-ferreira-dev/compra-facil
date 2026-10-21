// Package service contém os casos de uso do catálogo. Orquestra domain e repositórios; não conhece
// Gin nem GORM. Erros saem como erros de domínio (ValidationError, ErrNotFound, ErrConflict...).
package service

import (
	"context"
	"errors"
	"fmt"
	"strings"
	"time"
	"unicode/utf8"

	"github.com/google/uuid"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
)

// Clock permite fixar "agora" nos testes (vigência de campanhas).
type Clock func() time.Time

const maxLimit = 100

type ProductService struct {
	products   domain.ProductRepository
	categories domain.CategoryRepository
	now        Clock
}

func NewProductService(products domain.ProductRepository, categories domain.CategoryRepository, now Clock) *ProductService {
	return &ProductService{products: products, categories: categories, now: now}
}

// ListProductsInput espelha os parâmetros de GET /v1/products.
type ListProductsInput struct {
	Query         string
	CategorySlug  string
	MinPriceCents *int64
	MaxPriceCents *int64
	OnSale        bool
	Cursor        string
	Limit         int
}

// ListPublic lista só produtos ativos, como o cliente vê.
func (s *ProductService) ListPublic(ctx context.Context, in ListProductsInput) (domain.Page[domain.Product], error) {
	query := strings.TrimSpace(in.Query)
	if err := validateListInput(query, in); err != nil {
		return domain.Page[domain.Product]{}, err
	}

	active := domain.ProductActive
	filter := domain.ProductFilter{
		Status:        &active,
		Query:         query,
		MinPriceCents: in.MinPriceCents,
		MaxPriceCents: in.MaxPriceCents,
		OnSale:        in.OnSale,
		Now:           s.now(),
		Cursor:        in.Cursor,
		Limit:         in.Limit,
	}

	if in.CategorySlug != "" {
		ids, err := s.categorySubtree(ctx, in.CategorySlug)
		if errors.Is(err, domain.ErrNotFound) {
			// filtro por categoria inexistente: lista vazia, não erro
			return domain.Page[domain.Product]{Items: []domain.Product{}}, nil
		}
		if err != nil {
			return domain.Page[domain.Product]{}, err
		}
		filter.CategoryIDs = ids
	}

	return s.list(ctx, filter)
}

func validateListInput(query string, in ListProductsInput) error {
	var fields []domain.FieldError
	if n := utf8.RuneCountInString(query); query != "" && (n < 2 || n > 100) {
		fields = append(fields, domain.FieldError{Field: "q", Message: "deve ter entre 2 e 100 caracteres"})
	}
	if err := validateLimit(in.Limit); err != nil {
		fields = append(fields, domain.FieldError{Field: "limit", Message: err.Error()})
	}
	if in.MinPriceCents != nil && *in.MinPriceCents < 0 {
		fields = append(fields, domain.FieldError{Field: "min_price_cents", Message: "não pode ser negativo"})
	}
	if in.MaxPriceCents != nil && *in.MaxPriceCents < 0 {
		fields = append(fields, domain.FieldError{Field: "max_price_cents", Message: "não pode ser negativo"})
	}
	if in.MinPriceCents != nil && in.MaxPriceCents != nil && *in.MinPriceCents > *in.MaxPriceCents {
		fields = append(fields, domain.FieldError{Field: "max_price_cents", Message: "deve ser maior ou igual a min_price_cents"})
	}
	if len(fields) > 0 {
		return &domain.ValidationError{Fields: fields}
	}
	return nil
}

func (s *ProductService) categorySubtree(ctx context.Context, slug string) ([]uuid.UUID, error) {
	cat, err := s.categories.GetBySlug(ctx, slug)
	if err != nil {
		return nil, fmt.Errorf("categoria %q: %w", slug, err)
	}
	ids, err := s.categories.SubtreeIDs(ctx, cat.ID)
	if err != nil {
		return nil, fmt.Errorf("subcategorias de %q: %w", slug, err)
	}
	return ids, nil
}

// GetPublic devolve o produto só se estiver à venda; rascunho e inativo respondem 404.
func (s *ProductService) GetPublic(ctx context.Context, id uuid.UUID) (domain.Product, error) {
	p, err := s.products.GetByID(ctx, id, s.now())
	if err != nil {
		return domain.Product{}, fmt.Errorf("produto %s: %w", id, err)
	}
	if p.Status != domain.ProductActive {
		return domain.Product{}, fmt.Errorf("produto %s não está à venda: %w", id, domain.ErrNotFound)
	}
	return p, nil
}

// GetMany atende o gRPC (BatchGetProducts / ValidateItems): devolve qualquer status, o chamador decide.
func (s *ProductService) GetMany(ctx context.Context, ids []uuid.UUID) ([]domain.Product, error) {
	if len(ids) > maxLimit {
		return nil, domain.Invalid("product_ids", fmt.Sprintf("no máximo %d por chamada", maxLimit))
	}
	products, err := s.products.GetByIDs(ctx, ids, s.now())
	if err != nil {
		return nil, fmt.Errorf("buscando produtos: %w", err)
	}
	return products, nil
}

type AdminListInput struct {
	Status *domain.ProductStatus
	SKU    string
	Cursor string
	Limit  int
}

func (s *ProductService) AdminList(ctx context.Context, in AdminListInput) (domain.Page[domain.Product], error) {
	if in.Status != nil && !in.Status.Valid() {
		return domain.Page[domain.Product]{}, domain.Invalid("status", "deve ser draft, active ou inactive")
	}
	if err := validateLimit(in.Limit); err != nil {
		return domain.Page[domain.Product]{}, domain.Invalid("limit", err.Error())
	}
	return s.list(ctx, domain.ProductFilter{
		Status: in.Status, SKU: strings.TrimSpace(in.SKU), Now: s.now(), Cursor: in.Cursor, Limit: in.Limit,
	})
}

func (s *ProductService) AdminGet(ctx context.Context, id uuid.UUID) (domain.Product, error) {
	p, err := s.products.GetByID(ctx, id, s.now())
	if err != nil {
		return domain.Product{}, fmt.Errorf("produto %s: %w", id, err)
	}
	return p, nil
}

// ProductInput espelha o ProductInput do contrato (POST e PUT).
type ProductInput struct {
	SKU         string
	Name        string
	Description string
	CategoryID  uuid.UUID
	ImageURL    *string
	PriceCents  int64
	Status      domain.ProductStatus
}

func (in ProductInput) toProduct() domain.Product {
	p := domain.Product{
		SKU: in.SKU, Name: in.Name, Description: in.Description, CategoryID: in.CategoryID,
		ImageURL: in.ImageURL, PriceCents: in.PriceCents, Currency: domain.DefaultCurrency, Status: in.Status,
	}
	p.Normalize()
	return p
}

func (s *ProductService) Create(ctx context.Context, in ProductInput) (domain.Product, error) {
	p := in.toProduct()
	if err := p.Validate(); err != nil {
		return domain.Product{}, err
	}
	if err := s.products.Create(ctx, &p); err != nil {
		return domain.Product{}, productWriteError(err, p.SKU)
	}
	// relê para devolver categoria e campanha ativa preenchidas
	return s.AdminGet(ctx, p.ID)
}

// Update substitui os dados do produto se expectedVersion (do If-Match) for a versão atual.
func (s *ProductService) Update(ctx context.Context, id uuid.UUID, in ProductInput, expectedVersion int) (domain.Product, error) {
	p := in.toProduct()
	p.ID = id
	if err := p.Validate(); err != nil {
		return domain.Product{}, err
	}
	if err := s.products.Update(ctx, &p, expectedVersion); err != nil {
		return domain.Product{}, productWriteError(err, p.SKU)
	}
	return s.AdminGet(ctx, id)
}

func (s *ProductService) Delete(ctx context.Context, id uuid.UUID) error {
	if err := s.products.Delete(ctx, id); err != nil {
		return fmt.Errorf("removendo produto %s: %w", id, err)
	}
	return nil
}

func (s *ProductService) list(ctx context.Context, f domain.ProductFilter) (domain.Page[domain.Product], error) {
	page, err := s.products.List(ctx, f)
	if errors.Is(err, domain.ErrInvalidCursor) {
		return page, domain.Invalid("cursor", "cursor inválido ou de outra listagem")
	}
	if err != nil {
		return page, fmt.Errorf("listando produtos: %w", err)
	}
	return page, nil
}

// productWriteError troca erros de banco por mensagens que podem ir para o cliente.
func productWriteError(err error, sku string) error {
	switch {
	case errors.Is(err, domain.ErrInvalidReference):
		return domain.Invalid("category_id", "categoria não existe")
	case errors.Is(err, domain.ErrConflict):
		return fmt.Errorf("%w: SKU %s já cadastrado", domain.ErrConflict, sku)
	default:
		return fmt.Errorf("gravando produto: %w", err)
	}
}

func validateLimit(limit int) error {
	if limit < 0 || limit > maxLimit {
		return fmt.Errorf("deve estar entre 1 e %d", maxLimit)
	}
	return nil
}
