// Package service contém os casos de uso do catálogo. Orquestra domain e repositórios; não conhece
// Gin nem GORM. Erros saem como erros de domínio (ValidationError, ErrNotFound, ErrConflict...).
package service

import (
	"context"
	"errors"
	"fmt"
	"sort"
	"strings"
	"time"
	"unicode/utf8"

	"github.com/google/uuid"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
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
	// Brands: uma ou mais marcas (contrato: brand, separadas por vírgula).
	Brands []string
	// Types: um ou mais tipos de produto (slugs de categorias dentro de CategorySlug; contrato: type).
	Types []string
	// Sort: vazio é o padrão (relevância com Query, mais novos sem).
	Sort   domain.ProductSort
	Cursor string
	Limit  int
	// Page pagina por número (a partir de 1), com o total da listagem; alternativa ao Cursor.
	Page int
}

// MaxPage é a última página que a listagem por número aceita: o OFFSET cresce com ela, e ninguém
// percorre centenas de páginas de vitrine (a busca e os filtros servem para isso).
const MaxPage = 200

// maxBrandFilters é o máximo de marcas num filtro (contrato: brand, maxItems); vale também para type.
const maxBrandFilters = 20

// ListPublic lista só produtos ativos, como o cliente vê.
func (s *ProductService) ListPublic(ctx context.Context, in ListProductsInput) (domain.Page[domain.Product], error) {
	filter, found, err := s.publicFilter(ctx, in)
	if err != nil || !found {
		return domain.Page[domain.Product]{Items: []domain.Product{}}, err
	}
	return s.list(ctx, filter)
}

// ListBrands conta as marcas dos produtos ativos da listagem (os mesmos filtros, sem o de marca).
func (s *ProductService) ListBrands(ctx context.Context, in ListProductsInput) ([]domain.BrandCount, error) {
	in.Brands, in.Sort, in.Cursor, in.Limit, in.Page = nil, "", "", 0, 0
	filter, found, err := s.publicFilter(ctx, in)
	if err != nil || !found {
		return []domain.BrandCount{}, err
	}
	brands, err := s.products.Brands(ctx, filter, 0)
	if err != nil {
		return nil, fmt.Errorf("contando marcas: %w", err)
	}
	return brands, nil
}

// ListCategoryCounts conta, para cada categoria filha direta de in.CategorySlug, os produtos ativos
// dela e das descendentes que passam nos filtros da listagem (sem o de tipo): a faceta de tipo.
func (s *ProductService) ListCategoryCounts(ctx context.Context, in ListProductsInput) ([]domain.CategoryCount, error) {
	if strings.TrimSpace(in.CategorySlug) == "" {
		return nil, &domain.ValidationError{Fields: []domain.FieldError{{Field: "category", Message: "obrigatório"}}}
	}
	in.Types, in.Sort, in.Cursor, in.Limit, in.Page = nil, "", "", 0, 0
	filter, found, err := s.publicFilter(ctx, in)
	if err != nil || !found {
		return []domain.CategoryCount{}, err
	}
	counts, err := s.products.CountByCategory(ctx, filter)
	if err != nil {
		return nil, fmt.Errorf("contando por categoria: %w", err)
	}
	all, err := s.categories.List(ctx)
	if err != nil {
		return nil, fmt.Errorf("categorias: %w", err)
	}
	parent := make(map[uuid.UUID]uuid.UUID, len(all))
	var root uuid.UUID
	for _, c := range all {
		if c.ParentID != nil {
			parent[c.ID] = *c.ParentID
		}
		if c.Slug == in.CategorySlug {
			root = c.ID
		}
	}
	// cada categoria com produto soma na filha direta da raiz que é ancestral dela
	byChild := map[uuid.UUID]int{}
	for id, n := range counts {
		for c, depth := id, 0; depth < 16; depth++ {
			p, ok := parent[c]
			if !ok {
				break
			}
			if p == root {
				byChild[c] += n
				break
			}
			c = p
		}
	}
	out := []domain.CategoryCount{}
	for _, c := range all {
		if n := byChild[c.ID]; n > 0 && c.ParentID != nil && *c.ParentID == root {
			out = append(out, domain.CategoryCount{Slug: c.Slug, Name: c.Name, Count: n})
		}
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Name < out[j].Name })
	return out, nil
}

// publicFilter valida a entrada e monta o filtro dos produtos ativos. found é false quando a categoria
// não existe: a listagem é vazia, não um erro.
func (s *ProductService) publicFilter(ctx context.Context, in ListProductsInput) (domain.ProductFilter, bool, error) {
	query := strings.TrimSpace(in.Query)
	if err := validateListInput(query, in); err != nil {
		return domain.ProductFilter{}, false, err
	}

	active := domain.ProductActive
	filter := domain.ProductFilter{
		Status:        &active,
		Query:         query,
		MinPriceCents: in.MinPriceCents,
		MaxPriceCents: in.MaxPriceCents,
		OnSale:        in.OnSale,
		Brands:        in.Brands,
		Sort:          in.Sort,
		Now:           s.now(),
		Cursor:        in.Cursor,
		Limit:         in.Limit,
		Page:          in.Page,
	}

	if in.CategorySlug != "" {
		ids, err := s.categorySubtree(ctx, in.CategorySlug)
		if errors.Is(err, domain.ErrNotFound) {
			return filter, false, nil
		}
		if err != nil {
			return filter, false, err
		}
		filter.CategoryIDs = ids
	}
	if len(in.Types) > 0 {
		ids, err := s.typesSubtree(ctx, in.Types, filter.CategoryIDs)
		if err != nil {
			return filter, false, err
		}
		if len(ids) == 0 { // nenhum tipo existe dentro da categoria: listagem vazia
			return filter, false, nil
		}
		filter.CategoryIDs = ids
	}
	return filter, true, nil
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
	if in.Page < 0 || in.Page > MaxPage {
		fields = append(fields, domain.FieldError{Field: "page", Message: fmt.Sprintf("de 1 a %d", MaxPage)})
	}
	if in.Page > 0 && in.Cursor != "" {
		fields = append(fields, domain.FieldError{Field: "cursor", Message: "use cursor ou page, não os dois"})
	}
	if in.Sort != "" && !in.Sort.Valid() {
		fields = append(fields, domain.FieldError{Field: "sort", Message: "use relevance, price_asc, price_desc, discount ou newest"})
	}
	if len(in.Brands) > maxBrandFilters {
		fields = append(fields, domain.FieldError{Field: "brand", Message: fmt.Sprintf("no máximo %d marcas", maxBrandFilters)})
	}
	if len(in.Types) > maxBrandFilters {
		fields = append(fields, domain.FieldError{Field: "type", Message: fmt.Sprintf("no máximo %d tipos", maxBrandFilters)})
	}
	for _, t := range in.Types {
		if n := utf8.RuneCountInString(strings.TrimSpace(t)); n < 1 || n > 80 {
			fields = append(fields, domain.FieldError{Field: "type", Message: "cada tipo tem de 1 a 80 caracteres"})
			break
		}
	}
	for _, b := range in.Brands {
		if n := utf8.RuneCountInString(strings.TrimSpace(b)); n < 1 || n > domain.MaxBrandLength {
			fields = append(fields, domain.FieldError{Field: "brand", Message: fmt.Sprintf("cada marca tem de 1 a %d caracteres", domain.MaxBrandLength)})
			break
		}
	}
	if len(fields) > 0 {
		return &domain.ValidationError{Fields: fields}
	}
	return nil
}

// typesSubtree junta as subárvores dos tipos, só com o que fica dentro de within (a categoria da
// listagem), quando há uma. Tipo que não existe é ignorado.
func (s *ProductService) typesSubtree(ctx context.Context, types []string, within []uuid.UUID) ([]uuid.UUID, error) {
	allowed := map[uuid.UUID]bool{}
	for _, id := range within {
		allowed[id] = true
	}
	seen := map[uuid.UUID]bool{}
	var out []uuid.UUID
	for _, slug := range types {
		ids, err := s.categorySubtree(ctx, strings.TrimSpace(slug))
		if errors.Is(err, domain.ErrNotFound) {
			continue
		}
		if err != nil {
			return nil, err
		}
		for _, id := range ids {
			if !seen[id] && (len(within) == 0 || allowed[id]) {
				seen[id] = true
				out = append(out, id)
			}
		}
	}
	return out, nil
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
	// Query busca por nome, marca, descrição e SKU (a mesma busca da vitrine), em qualquer status.
	Query  string
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
	query := strings.TrimSpace(in.Query)
	if n := utf8.RuneCountInString(query); query != "" && (n < 2 || n > 100) {
		return domain.Page[domain.Product]{}, domain.Invalid("q", "deve ter entre 2 e 100 caracteres")
	}
	return s.list(ctx, domain.ProductFilter{
		Status: in.Status, Query: query, SKU: strings.TrimSpace(in.SKU), Now: s.now(), Cursor: in.Cursor, Limit: in.Limit,
	})
}

// AdminGetMany devolve os produtos dos ids em qualquer status (os produtos de uma campanha, até 1000).
func (s *ProductService) AdminGetMany(ctx context.Context, ids []uuid.UUID) ([]domain.Product, error) {
	if len(ids) > domain.MaxCampaignItems {
		return nil, domain.Invalid("product_ids", fmt.Sprintf("no máximo %d por chamada", domain.MaxCampaignItems))
	}
	products, err := s.products.GetByIDs(ctx, ids, s.now())
	if err != nil {
		return nil, fmt.Errorf("produtos %v: %w", ids, err)
	}
	return products, nil
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
	Brand       *string
	CategoryID  uuid.UUID
	ImageURL    *string
	PriceCents  int64
	Status      domain.ProductStatus
}

func (in ProductInput) toProduct() domain.Product {
	p := domain.Product{
		SKU: in.SKU, Name: in.Name, Description: in.Description, Brand: in.Brand, CategoryID: in.CategoryID,
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
