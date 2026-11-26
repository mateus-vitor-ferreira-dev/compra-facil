package repository

import (
	"context"
	"time"

	"github.com/google/uuid"
	"gorm.io/gorm"

	"github.com/mateus-vitor-ferreira-dev/embrulhado/backend/services/catalog/internal/domain"
)

type CategoryRepository struct{ db *gorm.DB }

func NewCategoryRepository(db *gorm.DB) *CategoryRepository { return &CategoryRepository{db: db} }

var _ domain.CategoryRepository = (*CategoryRepository)(nil)

func (r *CategoryRepository) Create(ctx context.Context, c *domain.Category) error {
	c.ID = newID(c.ID)
	rec := categoryRecord{ID: c.ID, Slug: c.Slug, Name: c.Name, ParentID: c.ParentID}
	if err := r.db.WithContext(ctx).Create(&rec).Error; err != nil {
		return mapError(err)
	}
	c.CreatedAt, c.UpdatedAt = rec.CreatedAt, rec.UpdatedAt
	return nil
}

// List devolve as categorias por nome, cada uma com os produtos ativos dela e das descendentes.
func (r *CategoryRepository) List(ctx context.Context) ([]domain.Category, error) {
	var rows []struct {
		ID        uuid.UUID
		Slug      string
		Name      string
		ParentID  *uuid.UUID
		CreatedAt time.Time
		UpdatedAt time.Time
		Direct    int
	}
	err := r.db.WithContext(ctx).Raw(`
		SELECT c.*, COALESCE(n.direct, 0) AS direct
		FROM categories c
		LEFT JOIN (
			SELECT category_id, count(*) AS direct FROM products
			WHERE deleted_at IS NULL AND status = 'active'
			GROUP BY category_id
		) n ON n.category_id = c.id
		ORDER BY c.name`).Scan(&rows).Error
	if err != nil {
		return nil, mapError(err)
	}
	out := make([]domain.Category, len(rows))
	index := make(map[uuid.UUID]int, len(rows))
	for i, row := range rows {
		out[i] = categoryRecord{ID: row.ID, Slug: row.Slug, Name: row.Name, ParentID: row.ParentID,
			CreatedAt: row.CreatedAt, UpdatedAt: row.UpdatedAt}.toDomain()
		index[row.ID] = i
	}
	// cada produto conta na categoria dele e em todas as ancestrais (a árvore é rasa; o limite evita laço)
	for _, row := range rows {
		for id, depth := &row.ID, 0; id != nil && depth < 16; depth++ {
			i, ok := index[*id]
			if !ok {
				break
			}
			out[i].ActiveProducts += row.Direct
			id = out[i].ParentID
		}
	}
	return out, nil
}

func (r *CategoryRepository) GetBySlug(ctx context.Context, slug string) (domain.Category, error) {
	var rec categoryRecord
	if err := r.db.WithContext(ctx).Where("slug = ?", slug).Take(&rec).Error; err != nil {
		return domain.Category{}, mapError(err)
	}
	return rec.toDomain(), nil
}

func (r *CategoryRepository) SubtreeIDs(ctx context.Context, id uuid.UUID) ([]uuid.UUID, error) {
	var ids []uuid.UUID
	err := r.db.WithContext(ctx).Raw(`
		WITH RECURSIVE subtree AS (
			SELECT id FROM categories WHERE id = ?
			UNION
			SELECT c.id FROM categories c JOIN subtree s ON c.parent_id = s.id
		)
		SELECT id FROM subtree`, id).Scan(&ids).Error
	if err != nil {
		return nil, mapError(err)
	}
	if len(ids) == 0 {
		return nil, domain.ErrNotFound
	}
	return ids, nil
}
