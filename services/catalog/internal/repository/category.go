package repository

import (
	"context"

	"github.com/google/uuid"
	"gorm.io/gorm"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
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

func (r *CategoryRepository) List(ctx context.Context) ([]domain.Category, error) {
	var recs []categoryRecord
	if err := r.db.WithContext(ctx).Order("name").Find(&recs).Error; err != nil {
		return nil, mapError(err)
	}
	out := make([]domain.Category, len(recs))
	for i, rec := range recs {
		out[i] = rec.toDomain()
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
