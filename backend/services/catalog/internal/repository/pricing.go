package repository

import (
	"context"
	"time"

	"github.com/google/uuid"
	"gorm.io/gorm"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

type pricingRuleRecord struct {
	CategoryID    *uuid.UUID `gorm:"type:uuid"`
	MarginPercent int
	MinPriceCents int64
	UpdatedAt     time.Time
}

func (pricingRuleRecord) TableName() string { return "pricing_rules" }

// PricingRuleRepository guarda as regras de margem (#217): uma por categoria e a padrão (sem categoria).
type PricingRuleRepository struct{ db *gorm.DB }

func NewPricingRuleRepository(db *gorm.DB) *PricingRuleRepository {
	return &PricingRuleRepository{db: db}
}

func (r *PricingRuleRepository) List(ctx context.Context) ([]domain.PricingRule, error) {
	var recs []pricingRuleRecord
	if err := r.db.WithContext(ctx).Order("updated_at").Find(&recs).Error; err != nil {
		return nil, mapError(err)
	}
	out := make([]domain.PricingRule, len(recs))
	for i, rec := range recs {
		out[i] = domain.PricingRule{CategoryID: rec.CategoryID, MarginPercent: rec.MarginPercent,
			MinPriceCents: rec.MinPriceCents, UpdatedAt: rec.UpdatedAt}
	}
	return out, nil
}

// Upsert grava a regra da categoria (ou a padrão, com CategoryID nulo), substituindo a anterior.
func (r *PricingRuleRepository) Upsert(ctx context.Context, rule *domain.PricingRule) error {
	rule.UpdatedAt = r.db.NowFunc()
	err := r.db.WithContext(ctx).Exec(`INSERT INTO pricing_rules (category_id, margin_percent, min_price_cents, updated_at)
		VALUES (?, ?, ?, ?)
		ON CONFLICT (category_id) DO UPDATE SET margin_percent = EXCLUDED.margin_percent,
			min_price_cents = EXCLUDED.min_price_cents, updated_at = EXCLUDED.updated_at`,
		rule.CategoryID, rule.MarginPercent, rule.MinPriceCents, rule.UpdatedAt).Error
	return mapError(err)
}

// Delete tira a regra da categoria (ela passa a seguir a do ancestral ou a padrão).
func (r *PricingRuleRepository) Delete(ctx context.Context, categoryID uuid.UUID) error {
	res := r.db.WithContext(ctx).Where("category_id = ?", categoryID).Delete(&pricingRuleRecord{})
	if res.Error != nil {
		return mapError(res.Error)
	}
	if res.RowsAffected == 0 {
		return domain.ErrNotFound
	}
	return nil
}
