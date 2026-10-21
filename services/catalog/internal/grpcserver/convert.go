package grpcserver

import (
	"google.golang.org/protobuf/types/known/timestamppb"

	catalogv1 "github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/gen/catalog/v1"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/service"
)

var productStatus = map[domain.ProductStatus]catalogv1.ProductStatus{
	domain.ProductDraft:    catalogv1.ProductStatus_PRODUCT_STATUS_DRAFT,
	domain.ProductActive:   catalogv1.ProductStatus_PRODUCT_STATUS_ACTIVE,
	domain.ProductInactive: catalogv1.ProductStatus_PRODUCT_STATUS_INACTIVE,
}

var itemStatus = map[service.ItemStatus]catalogv1.ItemStatus{
	service.ItemOK:              catalogv1.ItemStatus_ITEM_STATUS_OK,
	service.ItemNotFound:        catalogv1.ItemStatus_ITEM_STATUS_NOT_FOUND,
	service.ItemNotActive:       catalogv1.ItemStatus_ITEM_STATUS_NOT_ACTIVE,
	service.ItemPriceChanged:    catalogv1.ItemStatus_ITEM_STATUS_PRICE_CHANGED,
	service.ItemInvalidQuantity: catalogv1.ItemStatus_ITEM_STATUS_INVALID_QUANTITY,
}

func money(cents int64, currency string) *catalogv1.Money {
	return &catalogv1.Money{AmountCents: cents, Currency: currency}
}

func toProto(p domain.Product) *catalogv1.Product {
	out := &catalogv1.Product{
		Id:             p.ID.String(),
		Sku:            p.SKU,
		Name:           p.Name,
		Status:         productStatus[p.Status],
		Price:          money(p.PriceCents, p.Currency),
		EffectivePrice: money(p.EffectivePriceCents(), p.Currency),
		Version:        int32(p.Version), //nolint:gosec // versão cresce de 1 em 1; não chega perto de 2^31
		UpdatedAt:      timestamppb.New(p.UpdatedAt),
		CategoryId:     p.CategoryID.String(),
	}
	if c := p.ActiveCampaign; c != nil {
		out.Campaign = &catalogv1.CampaignRef{
			Id: c.ID.String(), Name: c.Name, DiscountPercent: int32(c.DiscountPercent), //nolint:gosec // 1..90
			EndsAt: timestamppb.New(c.EndsAt),
		}
	}
	return out
}

func toValidateResponse(v service.ItemsValidation) *catalogv1.ValidateItemsResponse {
	resp := &catalogv1.ValidateItemsResponse{
		Valid:    v.Valid,
		Items:    make([]*catalogv1.ValidatedItem, len(v.Items)),
		Total:    money(v.TotalCents, v.Currency),
		PricedAt: timestamppb.New(v.PricedAt),
	}
	for i, it := range v.Items {
		item := &catalogv1.ValidatedItem{
			ProductId: it.ProductID.String(),
			Status:    itemStatus[it.Status],
			Quantity:  int32(it.Quantity), //nolint:gosec // veio de um int32 do request
		}
		if p := it.Product; p != nil {
			item.Sku, item.Name = p.SKU, p.Name
			item.UnitPrice = money(it.UnitPriceCents, p.Currency)
			item.LineTotal = money(it.LineTotalCents, p.Currency)
			if p.ActiveCampaign != nil {
				item.CampaignId = p.ActiveCampaign.ID.String()
			}
		}
		resp.Items[i] = item
	}
	return resp
}
