package service

import (
	"context"
	"errors"
	"fmt"
	"sort"
	"time"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

// KeepOneActive é para a loja de demonstração: as campanhas do seed têm datas relativas ao dia da carga e acabam
// (a vitrine fica sem "Em promoção agora" e sem a campanha em destaque). Se nenhuma estiver ativa, a que terminou
// por último é reaberta a partir de agora, com a duração original; se os produtos dela cruzarem outra campanha
// nesse período, tenta a seguinte. Devolve a campanha reaberta, ou nil se já havia uma ativa (ou nenhuma serviu).
// Passa pelo Update: o CampaignUpdated vai ao outbox e o cache é invalidado por quem chamou.
func (s *CampaignService) KeepOneActive(ctx context.Context) (*domain.Campaign, error) {
	active, err := s.ListActive(ctx)
	if err != nil {
		return nil, err
	}
	if len(active) > 0 {
		return nil, nil //nolint:nilnil // nada a fazer não é erro
	}
	now := s.now().UTC()
	var ended []domain.Campaign
	cursor := ""
	for {
		page, err := s.campaigns.List(ctx, cursor, 100)
		if err != nil {
			return nil, fmt.Errorf("listando campanhas: %w", err)
		}
		for _, c := range page.Items {
			if !c.EndsAt.After(now) {
				ended = append(ended, c)
			}
		}
		if page.NextCursor == "" {
			break
		}
		cursor = page.NextCursor
	}
	// a que acabou por último primeiro: é a que a vitrine mostrava
	sort.Slice(ended, func(i, j int) bool { return ended[i].EndsAt.After(ended[j].EndsAt) })
	start := now.Truncate(time.Hour)
	for _, c := range ended {
		renewed, err := s.Update(ctx, c.ID, CampaignInput{
			Slug: c.Slug, Name: c.Name, DiscountPercent: c.DiscountPercent,
			StartsAt: start, EndsAt: start.Add(c.EndsAt.Sub(c.StartsAt)),
		})
		if errors.Is(err, domain.ErrConflict) {
			continue // os produtos dela estão noutra campanha nesse período
		}
		if err != nil {
			return nil, err
		}
		return &renewed, nil
	}
	return nil, nil //nolint:nilnil // nenhuma campanha para reabrir
}
