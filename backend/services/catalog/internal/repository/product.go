package repository

import (
	"context"
	"errors"
	"strings"
	"time"

	"github.com/google/uuid"
	"gorm.io/gorm"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/events"
)

const (
	// campanha vigente de maior desconto (empate: a que termina antes)
	activeCampaignJoin = `LEFT JOIN LATERAL (
		SELECT c.id, c.name, c.discount_percent, c.ends_at
		FROM campaign_products cp
		JOIN campaigns c ON c.id = cp.campaign_id
		WHERE cp.product_id = p.id AND c.starts_at <= @now AND c.ends_at > @now
		ORDER BY c.discount_percent DESC, c.ends_at ASC
		LIMIT 1
	) ac ON true`

	// mesma fórmula de domain.Product.EffectivePriceCents (divisão inteira arredonda para baixo)
	effectivePriceSQL = `(p.price_cents * (100 - COALESCE(ac.discount_percent, 0)) / 100)`

	// limiar do operador <% (padrão do pg_trgm: 0.6). 0.5 aceita 1 letra faltando em palavras de ~8 letras
	searchSimilarityThreshold = "0.5"

	// desconto da campanha vigente (0 sem campanha): a ordem "maior desconto"
	discountSQL = `COALESCE(ac.discount_percent, 0)`

	// marcas na faceta do filtro
	maxBrands = 50

	productColumns = `p.id, p.sku, p.name, p.description, p.brand, p.category_id, p.image_url, p.image, p.price_cents,
		p.currency, p.status, p.version, p.created_at, p.updated_at,
		cat.slug AS category_slug, cat.name AS category_name,
		ac.id AS campaign_id, ac.name AS campaign_name,
		ac.discount_percent AS campaign_discount, ac.ends_at AS campaign_ends_at`
)

type ProductRepository struct{ db *gorm.DB }

func NewProductRepository(db *gorm.DB) *ProductRepository { return &ProductRepository{db: db} }

var _ domain.ProductRepository = (*ProductRepository)(nil)

// Create grava o produto e o ProductCreated na mesma transação.
func (r *ProductRepository) Create(ctx context.Context, p *domain.Product) error {
	p.ID = newID(p.ID)
	p.Version = 1
	rec := productToRecord(p)
	err := r.db.WithContext(ctx).Transaction(func(tx *gorm.DB) error {
		if err := tx.Create(&rec).Error; err != nil {
			return err
		}
		p.Currency, p.CreatedAt, p.UpdatedAt = rec.Currency, rec.CreatedAt, rec.UpdatedAt
		msg, err := events.ProductCreated(ctx, *p, rec.CreatedAt)
		if err != nil {
			return err //nolint:wrapcheck // o builder já diz qual evento falhou
		}
		return enqueue(tx, msg)
	})
	return mapError(err)
}

// Update aplica a alteração se a versão bater e grava o ProductUpdated (mais o ProductPriceChanged,
// se o preço de tabela mudou) na mesma transação.
func (r *ProductRepository) Update(ctx context.Context, p *domain.Product, expectedVersion int) error {
	rec := productToRecord(p)
	now := r.db.NowFunc()
	err := r.db.WithContext(ctx).Transaction(func(tx *gorm.DB) error {
		// lê o preço anterior travando a linha: ninguém altera entre a leitura e o UPDATE
		var before productRecord
		if err := tx.Clauses(lockForUpdate).Select("price_cents", "currency").
			Where("id = ?", p.ID).Take(&before).Error; err != nil {
			return err
		}
		res := tx.Model(&productRecord{}).
			Where("id = ? AND version = ?", p.ID, expectedVersion).
			Updates(map[string]any{
				"sku":         rec.SKU,
				"name":        rec.Name,
				"description": rec.Description,
				"category_id": rec.CategoryID,
				"image_url":   rec.ImageURL,
				// as variantes eram da foto anterior: URL nova (definida pelo admin) fica sem elas. No
				// SET, image_url ainda é o valor de antes do UPDATE
				"image":       gorm.Expr("CASE WHEN image_url IS NOT DISTINCT FROM ? THEN image END", rec.ImageURL),
				"price_cents": rec.PriceCents,
				"currency":    rec.Currency,
				"status":      rec.Status,
				"version":     gorm.Expr("version + 1"),
				"updated_at":  now,
			})
		if res.Error != nil {
			return res.Error
		}
		if res.RowsAffected == 0 {
			return domain.ErrVersionConflict // a linha existe (travada acima), mas em outra versão
		}
		p.Version, p.Currency, p.UpdatedAt = expectedVersion+1, rec.Currency, now

		builders := []func() (events.Message, error){
			func() (events.Message, error) { return events.ProductUpdated(ctx, *p, now) },
		}
		if before.PriceCents != rec.PriceCents || before.Currency != rec.Currency {
			builders = append(builders, func() (events.Message, error) {
				return events.ProductPriceChanged(ctx, *p, before.PriceCents, before.Currency, now)
			})
		}
		msgs, err := buildAll(builders...)
		if err != nil {
			return err
		}
		return enqueue(tx, msgs...)
	})
	return mapError(err)
}

// Delete faz soft delete (deleted_at); o SKU fica livre para recadastro. A remoção sobe a versão,
// para o ProductDeleted não ser descartado pelo consumidor como evento velho.
func (r *ProductRepository) Delete(ctx context.Context, id uuid.UUID) error {
	now := r.db.NowFunc()
	err := r.db.WithContext(ctx).Transaction(func(tx *gorm.DB) error {
		var before productRecord
		if err := tx.Clauses(lockForUpdate).Select("sku", "version").
			Where("id = ?", id).Take(&before).Error; err != nil {
			return err
		}
		if err := tx.Model(&productRecord{}).Where("id = ?", id).Updates(map[string]any{
			"deleted_at": now,
			"version":    gorm.Expr("version + 1"),
			"updated_at": now,
		}).Error; err != nil {
			return err
		}
		msg, err := events.ProductDeleted(ctx, id, before.SKU, before.Version+1, now)
		if err != nil {
			return err //nolint:wrapcheck // o builder já diz qual evento falhou
		}
		return enqueue(tx, msg)
	})
	return mapError(err)
}

func (r *ProductRepository) GetByID(ctx context.Context, id uuid.UUID, now time.Time) (domain.Product, error) {
	var row productRow
	err := r.baseQuery(ctx, now).Where("p.id = ?", id).Take(&row).Error
	if err != nil {
		return domain.Product{}, mapError(err)
	}
	return row.toDomain(), nil
}

func (r *ProductRepository) GetByIDs(ctx context.Context, ids []uuid.UUID, now time.Time) ([]domain.Product, error) {
	if len(ids) == 0 {
		return nil, nil
	}
	var rows []productRow
	if err := r.baseQuery(ctx, now).Where("p.id IN ?", ids).Find(&rows).Error; err != nil {
		return nil, mapError(err)
	}
	return toProducts(rows), nil
}

func (r *ProductRepository) List(ctx context.Context, f domain.ProductFilter) (domain.Page[domain.Product], error) {
	cur, err := decodeCursor(f.Cursor)
	if err != nil {
		return domain.Page[domain.Product]{}, err
	}
	search := strings.ToLower(strings.TrimSpace(f.Query))
	sort := resolveSort(f.Sort, search != "")
	// o cursor é da ordem que o gerou (os de antes da ordenação não têm S: mais novos ou relevância)
	if f.Cursor != "" && cur.S != sort && (cur.S != "" || (sort != domain.SortNewest && sort != domain.SortRelevance)) {
		return domain.Page[domain.Product]{}, domain.ErrInvalidCursor
	}
	limit := clampLimit(f.Limit)
	q := applyFilters(r.baseQuery(ctx, f.Now), f)
	if len(f.Brands) > 0 {
		q = q.Where("lower(p.brand) IN ?", lowerAll(f.Brands))
	}
	if search != "" {
		// <% usa o índice trigram; word_similarity tolera erro de digitação e palavra parcial,
		// e f_unaccent faz "fogao" achar "Fogão"
		q = q.Where("f_unaccent(@q) <% p.search_text", map[string]any{"q": search})
	}

	switch sort {
	case domain.SortRelevance:
		q = q.Select(productColumns+", word_similarity(f_unaccent(@q), p.search_text) AS search_score", map[string]any{"q": search}).
			Order("search_score DESC, p.id").
			Offset(cur.O)
	case domain.SortPriceAsc:
		if cur.V != nil {
			q = q.Where("("+effectivePriceSQL+", p.id) > (?, ?)", *cur.V, cur.ID)
		}
		q = q.Order(effectivePriceSQL + " ASC, p.id ASC")
	case domain.SortPriceDesc:
		if cur.V != nil {
			q = q.Where("("+effectivePriceSQL+", p.id) < (?, ?)", *cur.V, cur.ID)
		}
		q = q.Order(effectivePriceSQL + " DESC, p.id DESC")
	case domain.SortDiscount:
		if cur.V != nil {
			q = q.Where("("+discountSQL+", p.id) < (?, ?)", *cur.V, cur.ID)
		}
		q = q.Order(discountSQL + " DESC, p.id DESC")
	default: // mais novos
		if cur.T != nil {
			q = q.Where("(p.created_at, p.id) < (?, ?)", *cur.T, cur.ID)
		}
		q = q.Order("p.created_at DESC, p.id DESC")
	}

	var rows []productRow
	if err := r.find(q.Limit(limit+1), search != "", &rows); err != nil {
		return domain.Page[domain.Product]{}, mapError(err)
	}

	page := domain.Page[domain.Product]{}
	if len(rows) > limit {
		rows = rows[:limit]
		if next, ok := nextCursor(sort, cur, rows[len(rows)-1], limit); ok {
			page.NextCursor = next.encode()
		}
	}
	page.Items = toProducts(rows)
	return page, nil
}

// Brands conta as marcas dos produtos que passam nos filtros da listagem (sem o de marca).
func (r *ProductRepository) Brands(ctx context.Context, f domain.ProductFilter, limit int) ([]domain.BrandCount, error) {
	if limit <= 0 || limit > maxBrands {
		limit = maxBrands
	}
	q := applyFilters(r.baseQuery(ctx, f.Now), f).
		Select("p.brand AS name, count(*) AS count").
		Where("p.brand IS NOT NULL").
		Group("p.brand").
		Order("count DESC, p.brand").
		Limit(limit)
	search := strings.ToLower(strings.TrimSpace(f.Query))
	if search != "" {
		q = q.Where("f_unaccent(@q) <% p.search_text", map[string]any{"q": search})
	}
	var out []domain.BrandCount
	if err := r.find(q, search != "", &out); err != nil {
		return nil, mapError(err)
	}
	return out, nil
}

// applyFilters aplica os filtros comuns à listagem e à faceta de marcas.
func applyFilters(q *gorm.DB, f domain.ProductFilter) *gorm.DB {
	if f.Status != nil {
		q = q.Where("p.status = ?", string(*f.Status))
	}
	if len(f.CategoryIDs) > 0 {
		q = q.Where("p.category_id IN ?", f.CategoryIDs)
	}
	if f.SKU != "" {
		q = q.Where("p.sku = ?", f.SKU)
	}
	if f.MinPriceCents != nil {
		q = q.Where(effectivePriceSQL+" >= ?", *f.MinPriceCents)
	}
	if f.MaxPriceCents != nil {
		q = q.Where(effectivePriceSQL+" <= ?", *f.MaxPriceCents)
	}
	if f.OnSale {
		q = q.Where("ac.id IS NOT NULL")
	}
	return q
}

// resolveSort aplica a ordem padrão: relevância com busca, mais novos sem (relevância sem busca
// também vira mais novos: não há o que pontuar).
func resolveSort(s domain.ProductSort, search bool) domain.ProductSort {
	switch {
	case s == "" && search, s == domain.SortRelevance && search:
		return domain.SortRelevance
	case s == "" || s == domain.SortRelevance:
		return domain.SortNewest
	default:
		return s
	}
}

// nextCursor aponta a página seguinte a partir do último item: o valor da ordem e o id (keyset), ou o
// offset na relevância (até maxSearchOffset).
func nextCursor(sort domain.ProductSort, cur cursor, last productRow, limit int) (cursor, bool) {
	p := last.toDomain()
	switch sort {
	case domain.SortRelevance:
		next := cursor{S: sort, O: cur.O + limit}
		return next, next.O <= maxSearchOffset
	case domain.SortPriceAsc, domain.SortPriceDesc:
		v := p.EffectivePriceCents()
		return cursor{S: sort, V: &v, ID: p.ID}, true
	case domain.SortDiscount:
		var v int64
		if p.ActiveCampaign != nil {
			v = int64(p.ActiveCampaign.DiscountPercent)
		}
		return cursor{S: sort, V: &v, ID: p.ID}, true
	default:
		return cursor{S: domain.SortNewest, T: &last.Rec.CreatedAt, ID: last.Rec.ID}, true
	}
}

func lowerAll(xs []string) []string {
	out := make([]string, len(xs))
	for i, x := range xs {
		out[i] = strings.ToLower(strings.TrimSpace(x))
	}
	return out
}

// find executa a consulta; na busca, ajusta o limiar de similaridade só nesta transação.
func (r *ProductRepository) find(q *gorm.DB, search bool, rows any) error {
	if !search {
		return q.Find(rows).Error
	}
	return q.Transaction(func(tx *gorm.DB) error {
		// SET LOCAL vale só até o fim da transação: não vaza para outras conexões do pool
		if err := tx.Session(&gorm.Session{NewDB: true}).
			Exec("SET LOCAL pg_trgm.word_similarity_threshold = " + searchSimilarityThreshold).Error; err != nil {
			return err
		}
		return tx.Find(rows).Error
	})
}

// baseQuery lê produtos não removidos com categoria e campanha ativa em `now`.
func (r *ProductRepository) baseQuery(ctx context.Context, now time.Time) *gorm.DB {
	if now.IsZero() {
		now = time.Now()
	}
	return r.db.WithContext(ctx).
		Table("products p").
		Select(productColumns).
		Joins("JOIN categories cat ON cat.id = p.category_id").
		Joins(activeCampaignJoin, map[string]any{"now": now}).
		Where("p.deleted_at IS NULL")
}

func toProducts(rows []productRow) []domain.Product {
	out := make([]domain.Product, len(rows))
	for i, row := range rows {
		out[i] = row.toDomain()
	}
	return out
}

// isNotFound é usado pelos outros repositórios para checar existência.
func isNotFound(err error) bool { return errors.Is(err, gorm.ErrRecordNotFound) }
