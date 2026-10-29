package service

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"strings"
	"time"

	"github.com/google/uuid"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

// SupplierProducts é o que o catálogo do fornecedor precisa do repositório de produtos.
type SupplierProducts interface {
	Create(ctx context.Context, p *domain.Product) error
	FindBySupplier(ctx context.Context, supplier, vid string, now time.Time) (domain.Product, error)
	UpdateFromSupplier(ctx context.Context, p *domain.Product, expectedVersion int) error
	ListFromSuppliers(ctx context.Context, after uuid.UUID, limit int) ([]domain.Product, error)
}

type PricingRules interface {
	List(ctx context.Context) ([]domain.PricingRule, error)
	Upsert(ctx context.Context, r *domain.PricingRule) error
	Delete(ctx context.Context, categoryID uuid.UUID) error
}

// SupplierOptions é a configuração do catálogo do fornecedor.
type SupplierOptions struct {
	// Rates é o câmbio de cada moeda dos fornecedores para o real.
	Rates domain.ExchangeRates
	// Categories leva a categoria do fornecedor (como vem no feed) ao slug da categoria da loja.
	Categories map[string]string
	// DefaultRule vale enquanto a equipe não grava a padrão no painel.
	DefaultRule domain.PricingRule
	// PublishNew põe o produto novo à venda na hora; desligado, ele nasce rascunho, para a equipe
	// traduzir e revisar antes.
	PublishNew bool
}

// SupplierProductUpdate é o SupplierProductUpdated do fulfillment (contracts/asyncapi/fulfillment.v1.yaml).
type SupplierProductUpdate struct {
	Supplier  string
	VID       string
	Name      string
	Category  string
	CostCents int64
	Currency  string
	ImageURL  *string
	UpdatedAt time.Time
}

// ErrUnmappedCategory: a categoria do fornecedor não tem categoria da loja na configuração. Não é
// mensagem envenenada: depois de configurar, o reprocessamento da DLT importa o produto.
var ErrUnmappedCategory = errors.New("categoria do fornecedor sem categoria da loja")

// SupplierCatalog aplica o feed do fornecedor ao catálogo (#217): cria o produto, reprecifica quando o
// custo muda e o tira de venda quando sai do feed. O que é do fornecedor (custo, disponibilidade) vem do
// feed; o que é da loja (nome, descrição, foto, categoria) a equipe edita, e o feed não sobrescreve.
type SupplierCatalog struct {
	products   SupplierProducts
	rules      PricingRules
	categories domain.CategoryRepository
	opts       SupplierOptions
	now        Clock
	log        *slog.Logger
}

func NewSupplierCatalog(products SupplierProducts, rules PricingRules, categories domain.CategoryRepository,
	opts SupplierOptions, now Clock, log *slog.Logger,
) *SupplierCatalog {
	return &SupplierCatalog{products: products, rules: rules, categories: categories, opts: opts, now: now, log: log}
}

// ApplyUpdate trata o produto novo, de volta ao feed ou alterado.
func (s *SupplierCatalog) ApplyUpdate(ctx context.Context, u SupplierProductUpdate) error {
	current, err := s.products.FindBySupplier(ctx, u.Supplier, u.VID, s.now())
	switch {
	case errors.Is(err, domain.ErrDeleted):
		return nil // a equipe removeu o produto: o feed não o traz de volta
	case errors.Is(err, domain.ErrNotFound):
		return s.create(ctx, u)
	case err != nil:
		return fmt.Errorf("produto %s/%s: %w", u.Supplier, u.VID, err)
	}
	if !u.UpdatedAt.After(current.Supplier.SyncedAt) {
		return nil // evento atrasado (retry, reprocessamento): não desfaz um mais novo
	}
	p := current
	supplier := *current.Supplier
	supplier.CostCents, supplier.CostCurrency, supplier.SyncedAt = u.CostCents, u.Currency, u.UpdatedAt
	if supplier.RemovedAt != nil {
		// voltou ao feed: o inativo volta à venda (foi a saída do feed que o desativou; rascunho segue rascunho)
		supplier.RemovedAt = nil
		if p.Status == domain.ProductInactive {
			p.Status = domain.ProductActive
		}
	}
	p.Supplier = &supplier
	price, err := s.pricer(ctx)
	if err != nil {
		return err
	}
	if p.PriceCents, err = price(p.CategoryID, supplier.CostCents, supplier.CostCurrency); err != nil {
		return err
	}
	if err := s.products.UpdateFromSupplier(ctx, &p, current.Version); err != nil {
		return fmt.Errorf("atualizando %s: %w", p.SKU, err)
	}
	return nil
}

// ApplyRemoval tira de venda o produto que saiu do feed.
func (s *SupplierCatalog) ApplyRemoval(ctx context.Context, supplier, vid string, removedAt time.Time) error {
	current, err := s.products.FindBySupplier(ctx, supplier, vid, s.now())
	if errors.Is(err, domain.ErrNotFound) || errors.Is(err, domain.ErrDeleted) {
		return nil // nunca entrou (categoria sem mapeamento) ou a equipe já removeu
	}
	if err != nil {
		return fmt.Errorf("produto %s/%s: %w", supplier, vid, err)
	}
	if !removedAt.After(current.Supplier.SyncedAt) {
		return nil
	}
	p := current
	info := *current.Supplier
	info.RemovedAt, info.SyncedAt = &removedAt, removedAt
	p.Supplier = &info
	if p.Status == domain.ProductActive {
		p.Status = domain.ProductInactive
	}
	if err := s.products.UpdateFromSupplier(ctx, &p, current.Version); err != nil {
		return fmt.Errorf("tirando %s de venda: %w", p.SKU, err)
	}
	return nil
}

func (s *SupplierCatalog) create(ctx context.Context, u SupplierProductUpdate) error {
	slug, ok := s.opts.Categories[u.Category]
	if !ok {
		return fmt.Errorf("%w: %q (configure supplier.categories)", ErrUnmappedCategory, u.Category)
	}
	cat, err := s.categories.GetBySlug(ctx, slug)
	if err != nil {
		return fmt.Errorf("categoria %q da loja: %w", slug, err)
	}
	status := domain.ProductDraft
	if s.opts.PublishNew {
		status = domain.ProductActive
	}
	p := domain.Product{
		SKU: strings.ToUpper(u.VID), Name: u.Name, CategoryID: cat.ID, ImageURL: u.ImageURL, Status: status,
		Currency: domain.DefaultCurrency,
		Supplier: &domain.SupplierInfo{Name: u.Supplier, VID: u.VID, CostCents: u.CostCents, CostCurrency: u.Currency, SyncedAt: u.UpdatedAt},
	}
	p.Normalize()
	price, err := s.pricer(ctx)
	if err != nil {
		return err
	}
	if p.PriceCents, err = price(cat.ID, u.CostCents, u.Currency); err != nil {
		return err
	}
	if err := p.Validate(); err != nil {
		return fmt.Errorf("produto %s do fornecedor: %w", u.VID, err)
	}
	if err := s.products.Create(ctx, &p); err != nil {
		return fmt.Errorf("criando %s: %w", p.SKU, err)
	}
	s.log.InfoContext(ctx, "produto do fornecedor entrou no catálogo", "sku", p.SKU, "status", p.Status, "price_cents", p.PriceCents)
	return nil
}

// pricer lê as regras e a árvore de categorias uma vez e devolve o cálculo do preço de venda: a regra
// da categoria, ou a do ancestral mais próximo, ou a padrão.
func (s *SupplierCatalog) pricer(ctx context.Context) (func(categoryID uuid.UUID, costCents int64, currency string) (int64, error), error) {
	rules, err := s.rules.List(ctx)
	if err != nil {
		return nil, fmt.Errorf("regras de margem: %w", err)
	}
	parent, err := s.parents(ctx)
	if err != nil {
		return nil, err
	}
	return func(categoryID uuid.UUID, costCents int64, currency string) (int64, error) {
		price, err := domain.SalePriceCents(costCents, currency, s.opts.Rates, domain.RuleFor(categoryID, rules, parent, s.opts.DefaultRule))
		if err != nil {
			return 0, fmt.Errorf("preço de venda: %w", err)
		}
		return price, nil
	}, nil
}

func (s *SupplierCatalog) parents(ctx context.Context) (map[uuid.UUID]uuid.UUID, error) {
	all, err := s.categories.List(ctx)
	if err != nil {
		return nil, fmt.Errorf("categorias: %w", err)
	}
	parent := make(map[uuid.UUID]uuid.UUID, len(all))
	for _, c := range all {
		if c.ParentID != nil {
			parent[c.ID] = *c.ParentID
		}
	}
	return parent, nil
}

// PricingSettings é o que o painel mostra: as regras gravadas, a padrão em vigor e o câmbio.
type PricingSettings struct {
	Default domain.PricingRule
	Rules   []domain.PricingRule
	Rates   domain.ExchangeRates
}

func (s *SupplierCatalog) Settings(ctx context.Context) (PricingSettings, error) {
	all, err := s.rules.List(ctx)
	if err != nil {
		return PricingSettings{}, fmt.Errorf("regras de margem: %w", err)
	}
	out := PricingSettings{Default: s.opts.DefaultRule, Rules: []domain.PricingRule{}, Rates: s.opts.Rates}
	for _, r := range all {
		if r.CategoryID == nil {
			out.Default = r
		} else {
			out.Rules = append(out.Rules, r)
		}
	}
	return out, nil
}

// SetRule grava a regra (CategoryID nulo: a padrão) e reprecifica os produtos de fornecedor.
func (s *SupplierCatalog) SetRule(ctx context.Context, rule domain.PricingRule) (domain.PricingRule, int, error) {
	if err := rule.Validate(); err != nil {
		return domain.PricingRule{}, 0, err
	}
	if err := s.rules.Upsert(ctx, &rule); err != nil {
		if errors.Is(err, domain.ErrInvalidReference) {
			return domain.PricingRule{}, 0, domain.Invalid("category_id", "categoria não existe")
		}
		return domain.PricingRule{}, 0, fmt.Errorf("gravando a regra: %w", err)
	}
	n, err := s.Reprice(ctx)
	return rule, n, err
}

// DeleteRule tira a regra da categoria e reprecifica.
func (s *SupplierCatalog) DeleteRule(ctx context.Context, categoryID uuid.UUID) (int, error) {
	if err := s.rules.Delete(ctx, categoryID); err != nil {
		return 0, fmt.Errorf("regra da categoria %s: %w", categoryID, err)
	}
	return s.Reprice(ctx)
}

const repriceBatch = 200

// Reprice aplica as regras de agora a todos os produtos de fornecedor; devolve quantos mudaram de preço.
func (s *SupplierCatalog) Reprice(ctx context.Context) (int, error) {
	price, err := s.pricer(ctx)
	if err != nil {
		return 0, err
	}
	changed := 0
	after := uuid.Nil
	for {
		batch, err := s.products.ListFromSuppliers(ctx, after, repriceBatch)
		if err != nil {
			return changed, fmt.Errorf("produtos de fornecedor: %w", err)
		}
		for _, p := range batch {
			cents, err := price(p.CategoryID, p.Supplier.CostCents, p.Supplier.CostCurrency)
			if err != nil {
				return changed, err
			}
			if cents == p.PriceCents {
				continue
			}
			version := p.Version
			p.PriceCents = cents
			if err := s.products.UpdateFromSupplier(ctx, &p, version); err != nil {
				return changed, fmt.Errorf("reprecificando %s: %w", p.SKU, err)
			}
			changed++
		}
		if len(batch) < repriceBatch {
			return changed, nil
		}
		after = batch[len(batch)-1].ID
	}
}
