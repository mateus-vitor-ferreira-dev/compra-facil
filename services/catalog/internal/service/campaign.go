package service

import (
	"context"
	"errors"
	"fmt"
	"strings"
	"time"

	"github.com/google/uuid"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
)

// CampaignService cuida das campanhas promocionais (time de Catálogo e campanhas).
//
// Regra: um produto só participa de uma campanha por período. A checagem é feita aqui, antes de
// gravar; duas alterações administrativas simultâneas no mesmo produto podem escapar dela, e nesse
// caso a leitura usa a de maior desconto (ver repository.activeCampaignJoin).
type CampaignService struct {
	campaigns domain.CampaignRepository
	now       Clock
}

func NewCampaignService(campaigns domain.CampaignRepository, now Clock) *CampaignService {
	return &CampaignService{campaigns: campaigns, now: now}
}

func (s *CampaignService) ListActive(ctx context.Context) ([]domain.Campaign, error) {
	cs, err := s.campaigns.ListActive(ctx, s.now())
	if err != nil {
		return nil, fmt.Errorf("listando campanhas ativas: %w", err)
	}
	return cs, nil
}

// NextStart diz quando começa a próxima campanha (zero se nenhuma): o cache não pode passar dele,
// porque o preço efetivo dos produtos dela muda nesse instante.
func (s *CampaignService) NextStart(ctx context.Context) (time.Time, error) {
	next, err := s.campaigns.NextStart(ctx, s.now())
	if err != nil {
		return time.Time{}, fmt.Errorf("buscando a próxima campanha: %w", err)
	}
	return next, nil
}

func (s *CampaignService) AdminList(ctx context.Context, cursor string, limit int) (domain.Page[domain.Campaign], error) {
	if err := validateLimit(limit); err != nil {
		return domain.Page[domain.Campaign]{}, domain.Invalid("limit", err.Error())
	}
	page, err := s.campaigns.List(ctx, cursor, limit)
	if errors.Is(err, domain.ErrInvalidCursor) {
		return page, domain.Invalid("cursor", "cursor inválido ou de outra listagem")
	}
	if err != nil {
		return page, fmt.Errorf("listando campanhas: %w", err)
	}
	return page, nil
}

type CampaignInput struct {
	Slug            string
	Name            string
	DiscountPercent int
	StartsAt        time.Time
	EndsAt          time.Time
}

func (in CampaignInput) toCampaign() domain.Campaign {
	return domain.Campaign{
		Slug: strings.TrimSpace(in.Slug), Name: strings.TrimSpace(in.Name), DiscountPercent: in.DiscountPercent,
		StartsAt: in.StartsAt.UTC(), EndsAt: in.EndsAt.UTC(),
	}
}

func (s *CampaignService) Create(ctx context.Context, in CampaignInput) (domain.Campaign, error) {
	c := in.toCampaign()
	if err := c.Validate(); err != nil {
		return domain.Campaign{}, err
	}
	if err := s.campaigns.Create(ctx, &c); err != nil {
		return domain.Campaign{}, campaignWriteError(err, c.Slug)
	}
	return c, nil
}

// Update altera a campanha; se mudar o período, os produtos dela não podem cruzar outra campanha.
func (s *CampaignService) Update(ctx context.Context, id uuid.UUID, in CampaignInput) (domain.Campaign, error) {
	c := in.toCampaign()
	c.ID = id
	if err := c.Validate(); err != nil {
		return domain.Campaign{}, err
	}
	if err := s.checkOverlap(ctx, id, c.StartsAt, c.EndsAt, nil); err != nil {
		return domain.Campaign{}, err
	}
	if err := s.campaigns.Update(ctx, &c); err != nil {
		return domain.Campaign{}, campaignWriteError(err, c.Slug)
	}
	return s.Get(ctx, id)
}

func (s *CampaignService) Get(ctx context.Context, id uuid.UUID) (domain.Campaign, error) {
	c, err := s.campaigns.GetByID(ctx, id)
	if err != nil {
		return domain.Campaign{}, fmt.Errorf("campanha %s: %w", id, err)
	}
	return c, nil
}

func (s *CampaignService) Delete(ctx context.Context, id uuid.UUID) error {
	if err := s.campaigns.Delete(ctx, id); err != nil {
		return fmt.Errorf("removendo campanha %s: %w", id, err)
	}
	return nil
}

// SetProducts substitui os produtos da campanha.
func (s *CampaignService) SetProducts(ctx context.Context, id uuid.UUID, productIDs []uuid.UUID) error {
	if len(productIDs) > domain.MaxCampaignItems {
		return domain.Invalid("product_ids", fmt.Sprintf("no máximo %d produtos", domain.MaxCampaignItems))
	}
	if dup, ok := firstDuplicate(productIDs); ok {
		return domain.Invalid("product_ids", "produto repetido: "+dup.String())
	}
	if productIDs == nil {
		productIDs = []uuid.UUID{} // nil teria outro significado em OverlappingProducts
	}

	c, err := s.Get(ctx, id)
	if err != nil {
		return err
	}
	if err := s.checkOverlap(ctx, id, c.StartsAt, c.EndsAt, productIDs); err != nil {
		return err
	}

	err = s.campaigns.SetProducts(ctx, id, productIDs)
	switch {
	case errors.Is(err, domain.ErrInvalidReference):
		return domain.Invalid("product_ids", "algum produto não existe")
	case err != nil:
		return fmt.Errorf("gravando produtos da campanha %s: %w", id, err)
	}
	return nil
}

func (s *CampaignService) checkOverlap(ctx context.Context, id uuid.UUID, start, end time.Time, productIDs []uuid.UUID) error {
	clash, err := s.campaigns.OverlappingProducts(ctx, id, start, end, productIDs)
	if err != nil {
		return fmt.Errorf("verificando sobreposição de campanhas: %w", err)
	}
	if len(clash) == 0 {
		return nil
	}
	ids := make([]string, len(clash))
	for i, pid := range clash {
		ids[i] = pid.String()
	}
	return fmt.Errorf("%w: produtos já estão em outra campanha no período: %s",
		domain.ErrConflict, strings.Join(ids, ", "))
}

func campaignWriteError(err error, slug string) error {
	switch {
	case errors.Is(err, domain.ErrConflict):
		return fmt.Errorf("%w: slug %s já cadastrado", domain.ErrConflict, slug)
	case errors.Is(err, domain.ErrNotFound):
		return err
	default:
		return fmt.Errorf("gravando campanha: %w", err)
	}
}

func firstDuplicate(ids []uuid.UUID) (uuid.UUID, bool) {
	seen := make(map[uuid.UUID]struct{}, len(ids))
	for _, id := range ids {
		if _, ok := seen[id]; ok {
			return id, true
		}
		seen[id] = struct{}{}
	}
	return uuid.Nil, false
}
