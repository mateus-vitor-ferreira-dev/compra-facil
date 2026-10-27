package repository

import (
	"context"
	"database/sql"
	"time"

	"github.com/google/uuid"
	"gorm.io/gorm"
	"gorm.io/gorm/clause"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/events"
)

const campaignColumns = `campaigns.*,
	(SELECT count(*) FROM campaign_products cp WHERE cp.campaign_id = campaigns.id) AS product_count`

type CampaignRepository struct{ db *gorm.DB }

func NewCampaignRepository(db *gorm.DB) *CampaignRepository { return &CampaignRepository{db: db} }

var _ domain.CampaignRepository = (*CampaignRepository)(nil)

// Create grava a campanha (versão 1, sem produtos) e o CampaignCreated na mesma transação.
func (r *CampaignRepository) Create(ctx context.Context, c *domain.Campaign) error {
	c.ID = newID(c.ID)
	c.Version = 1
	rec := campaignRecord{
		ID: c.ID, Slug: c.Slug, Name: c.Name, DiscountPercent: c.DiscountPercent,
		StartsAt: c.StartsAt, EndsAt: c.EndsAt, Version: c.Version,
	}
	err := r.db.WithContext(ctx).Transaction(func(tx *gorm.DB) error {
		if err := tx.Create(&rec).Error; err != nil {
			return err
		}
		c.CreatedAt, c.UpdatedAt = rec.CreatedAt, rec.UpdatedAt
		msg, err := events.CampaignCreated(ctx, *c, nil, rec.CreatedAt)
		if err != nil {
			return err //nolint:wrapcheck // o builder já diz qual evento falhou
		}
		return enqueue(tx, msg)
	})
	return mapError(err)
}

// Update altera os dados da campanha, sobe a versão e grava o CampaignUpdated com a lista atual
// de produtos, tudo na mesma transação.
func (r *CampaignRepository) Update(ctx context.Context, c *domain.Campaign) error {
	now := r.db.NowFunc()
	err := r.db.WithContext(ctx).Transaction(func(tx *gorm.DB) error {
		var after campaignRecord
		res := tx.Model(&after).Clauses(clause.Returning{Columns: []clause.Column{{Name: "version"}}}).
			Where("id = ?", c.ID).Updates(map[string]any{
			"slug":             c.Slug,
			"name":             c.Name,
			"discount_percent": c.DiscountPercent,
			"starts_at":        c.StartsAt,
			"ends_at":          c.EndsAt,
			"version":          gorm.Expr("version + 1"),
			"updated_at":       now,
		})
		if res.Error != nil {
			return res.Error
		}
		if res.RowsAffected == 0 {
			return domain.ErrNotFound
		}
		c.Version, c.UpdatedAt = after.Version, now

		productIDs, err := campaignProductIDs(tx, c.ID)
		if err != nil {
			return err
		}
		msg, err := events.CampaignUpdated(ctx, *c, productIDs, now)
		if err != nil {
			return err //nolint:wrapcheck // o builder já diz qual evento falhou
		}
		return enqueue(tx, msg)
	})
	return mapError(err)
}

// Delete remove a campanha (e os vínculos, por cascade) e grava o CampaignDeleted.
func (r *CampaignRepository) Delete(ctx context.Context, id uuid.UUID) error {
	now := r.db.NowFunc()
	err := r.db.WithContext(ctx).Transaction(func(tx *gorm.DB) error {
		var before campaignRecord
		if err := tx.Clauses(lockForUpdate).Select("version").Where("id = ?", id).Take(&before).Error; err != nil {
			return err
		}
		if err := tx.Delete(&campaignRecord{}, "id = ?", id).Error; err != nil {
			return err
		}
		msg, err := events.CampaignDeleted(ctx, id, before.Version+1, now)
		if err != nil {
			return err //nolint:wrapcheck // o builder já diz qual evento falhou
		}
		return enqueue(tx, msg)
	})
	return mapError(err)
}

func campaignProductIDs(tx *gorm.DB, campaignID uuid.UUID) ([]uuid.UUID, error) {
	var ids []uuid.UUID
	err := tx.Model(&campaignProductRecord{}).Where("campaign_id = ?", campaignID).
		Order("product_id").Pluck("product_id", &ids).Error
	return ids, err //nolint:wrapcheck // traduzido por mapError em quem chama
}

func (r *CampaignRepository) GetByID(ctx context.Context, id uuid.UUID) (domain.Campaign, error) {
	var rec campaignRecord
	if err := r.db.WithContext(ctx).Select(campaignColumns).Where("id = ?", id).Take(&rec).Error; err != nil {
		return domain.Campaign{}, mapError(err)
	}
	return rec.toDomain(), nil
}

func (r *CampaignRepository) ListActive(ctx context.Context, now time.Time) ([]domain.Campaign, error) {
	var recs []campaignRecord
	err := r.db.WithContext(ctx).Select(campaignColumns).
		Where("starts_at <= ? AND ends_at > ?", now, now).
		Order("ends_at, id").
		Find(&recs).Error
	if err != nil {
		return nil, mapError(err)
	}
	return toCampaigns(recs), nil
}

func (r *CampaignRepository) NextStart(ctx context.Context, now time.Time) (time.Time, error) {
	var next sql.NullTime // min() de nenhuma linha é NULL
	err := r.db.WithContext(ctx).Model(&campaignRecord{}).
		Select("min(starts_at)").Where("starts_at > ?", now).
		Row().Scan(&next)
	if err != nil {
		return time.Time{}, mapError(err)
	}
	if !next.Valid {
		return time.Time{}, nil
	}
	return next.Time.UTC(), nil
}

// List ordena das campanhas que começam mais tarde para as mais antigas.
func (r *CampaignRepository) List(ctx context.Context, cursorStr string, limit int) (domain.Page[domain.Campaign], error) {
	cur, err := decodeCursor(cursorStr)
	if err != nil {
		return domain.Page[domain.Campaign]{}, err
	}
	limit = clampLimit(limit)

	q := r.db.WithContext(ctx).Select(campaignColumns).Order("starts_at DESC, id DESC")
	if cur.T != nil {
		q = q.Where("(starts_at, id) < (?, ?)", *cur.T, cur.ID)
	}
	var recs []campaignRecord
	if err := q.Limit(limit + 1).Find(&recs).Error; err != nil {
		return domain.Page[domain.Campaign]{}, mapError(err)
	}

	page := domain.Page[domain.Campaign]{}
	if len(recs) > limit {
		recs = recs[:limit]
		last := recs[len(recs)-1]
		page.NextCursor = cursor{T: &last.StartsAt, ID: last.ID}.encode()
	}
	page.Items = toCampaigns(recs)
	return page, nil
}

// SetProducts troca a lista de produtos, sobe a versão e grava o CampaignUpdated com a lista nova.
func (r *CampaignRepository) ProductIDs(ctx context.Context, campaignID uuid.UUID) ([]uuid.UUID, error) {
	var ids []uuid.UUID
	err := r.db.WithContext(ctx).Table("campaign_products cp").
		Joins("JOIN products p ON p.id = cp.product_id AND p.deleted_at IS NULL").
		Where("cp.campaign_id = ?", campaignID).
		Order("p.name, p.id").
		Pluck("cp.product_id", &ids).Error
	return ids, mapError(err)
}

func (r *CampaignRepository) SetProducts(ctx context.Context, campaignID uuid.UUID, productIDs []uuid.UUID) error {
	now := r.db.NowFunc()
	err := r.db.WithContext(ctx).Transaction(func(tx *gorm.DB) error {
		// trava a campanha: duas substituições simultâneas não se misturam
		var rec campaignRecord
		if err := tx.Clauses(lockForUpdate).Where("id = ?", campaignID).Take(&rec).Error; err != nil {
			return err
		}
		if err := tx.Where("campaign_id = ?", campaignID).Delete(&campaignProductRecord{}).Error; err != nil {
			return err
		}
		if len(productIDs) > 0 {
			links := make([]campaignProductRecord, len(productIDs))
			for i, pid := range productIDs {
				links[i] = campaignProductRecord{CampaignID: campaignID, ProductID: pid}
			}
			if err := tx.CreateInBatches(links, 500).Error; err != nil {
				return err
			}
		}
		if err := tx.Model(&campaignRecord{}).Where("id = ?", campaignID).Updates(map[string]any{
			"version":    gorm.Expr("version + 1"),
			"updated_at": now,
		}).Error; err != nil {
			return err
		}
		c := rec.toDomain()
		c.Version, c.UpdatedAt = rec.Version+1, now
		msg, err := events.CampaignUpdated(ctx, c, productIDs, now)
		if err != nil {
			return err //nolint:wrapcheck // o builder já diz qual evento falhou
		}
		return enqueue(tx, msg)
	})
	if isNotFound(err) {
		return domain.ErrNotFound
	}
	return mapError(err)
}

func (r *CampaignRepository) OverlappingProducts(
	ctx context.Context, campaignID uuid.UUID, start, end time.Time, productIDs []uuid.UUID,
) ([]uuid.UUID, error) {
	q := r.db.WithContext(ctx).
		Table("campaign_products cp").
		Distinct("cp.product_id").
		Joins("JOIN campaigns c ON c.id = cp.campaign_id").
		Where("c.id <> ? AND c.starts_at < ? AND c.ends_at > ?", campaignID, end, start)
	if productIDs == nil {
		q = q.Where("cp.product_id IN (SELECT product_id FROM campaign_products WHERE campaign_id = ?)", campaignID)
	} else {
		if len(productIDs) == 0 {
			return nil, nil
		}
		q = q.Where("cp.product_id IN ?", productIDs)
	}

	var ids []uuid.UUID
	if err := q.Order("cp.product_id").Pluck("cp.product_id", &ids).Error; err != nil {
		return nil, mapError(err)
	}
	return ids, nil
}

func toCampaigns(recs []campaignRecord) []domain.Campaign {
	out := make([]domain.Campaign, len(recs))
	for i, rec := range recs {
		out[i] = rec.toDomain()
	}
	return out
}
