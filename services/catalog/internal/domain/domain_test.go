package domain

import (
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
)

func TestProduct_EffectivePriceCents(t *testing.T) {
	tests := []struct {
		name     string
		price    int64
		discount int // 0 = sem campanha
		want     int64
	}{
		{"sem campanha", 19990, 0, 19990},
		{"30% exato", 10000, 30, 7000},
		{"arredonda para baixo (a favor do cliente)", 999, 15, 849}, // 849,15
		{"desconto máximo", 10000, 90, 1000},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			p := Product{PriceCents: tt.price}
			if tt.discount > 0 {
				p.ActiveCampaign = &CampaignRef{DiscountPercent: tt.discount}
			}
			assert.Equal(t, tt.want, p.EffectivePriceCents())
		})
	}
}

func TestCampaign_ActiveAt(t *testing.T) {
	start := time.Date(2026, 11, 27, 0, 0, 0, 0, time.UTC)
	c := Campaign{StartsAt: start, EndsAt: start.Add(24 * time.Hour)}

	assert.False(t, c.ActiveAt(start.Add(-time.Nanosecond)), "antes do início")
	assert.True(t, c.ActiveAt(start), "início é inclusivo")
	assert.True(t, c.ActiveAt(start.Add(23*time.Hour)))
	assert.False(t, c.ActiveAt(c.EndsAt), "fim é exclusivo")
}
