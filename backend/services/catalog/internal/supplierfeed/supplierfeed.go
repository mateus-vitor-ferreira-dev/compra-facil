// Package supplierfeed liga os eventos do feed do fornecedor (fulfillment.supplier-product.v1,
// contracts/asyncapi/fulfillment.v1.yaml) ao catálogo: cada evento roda o caso de uso dentro da
// transação do inbox, com os repositórios dela.
package supplierfeed

import (
	"context"
	"fmt"
	"log/slog"
	"time"

	"gorm.io/gorm"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/messaging"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/repository"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/service"
)

const (
	Topic          = "fulfillment.supplier-product.v1"
	ProductUpdated = "SupplierProductUpdated"
	ProductRemoved = "SupplierProductRemoved"
)

type money struct {
	AmountCents int64  `json:"amount_cents"`
	Currency    string `json:"currency"`
}

type updatedData struct {
	Supplier  string         `json:"supplier"`
	VID       string         `json:"vid"`
	Name      string         `json:"name"`
	Category  string         `json:"category"`
	Cost      money          `json:"cost"`
	ImageURL  *string        `json:"image_url"`
	UpdatedAt messaging.Time `json:"updated_at"`
}

type removedData struct {
	Supplier  string         `json:"supplier"`
	VID       string         `json:"vid"`
	RemovedAt messaging.Time `json:"removed_at"`
}

// Catalog monta o caso de uso sobre a transação do evento.
func Catalog(tx *gorm.DB, opts service.SupplierOptions, log *slog.Logger) *service.SupplierCatalog {
	return service.NewSupplierCatalog(repository.NewProductRepository(tx), repository.NewPricingRuleRepository(tx),
		repository.NewCategoryRepository(tx), opts, time.Now, log)
}

// Register registra os handlers do feed no consumer.
func Register(c *messaging.Consumer, opts service.SupplierOptions, log *slog.Logger) {
	c.Handle(Topic, ProductUpdated, func(ctx context.Context, tx *gorm.DB, e messaging.Event) error {
		var d updatedData
		if err := e.Decode(&d); err != nil {
			return err //nolint:wrapcheck // Decode já diz qual evento
		}
		if d.Supplier == "" || d.VID == "" || d.Cost.AmountCents <= 0 || d.Cost.Currency == "" || d.UpdatedAt.IsZero() {
			return fmt.Errorf("%w: %s sem supplier, vid, custo ou updated_at", messaging.ErrPoison, e.EventType)
		}
		return Catalog(tx, opts, log).ApplyUpdate(ctx, service.SupplierProductUpdate{ //nolint:wrapcheck // o caso de uso já descreve
			Supplier: d.Supplier, VID: d.VID, Name: d.Name, Category: d.Category, CostCents: d.Cost.AmountCents,
			Currency: d.Cost.Currency, ImageURL: d.ImageURL, UpdatedAt: d.UpdatedAt.Time,
		})
	})
	c.Handle(Topic, ProductRemoved, func(ctx context.Context, tx *gorm.DB, e messaging.Event) error {
		var d removedData
		if err := e.Decode(&d); err != nil {
			return err //nolint:wrapcheck // Decode já diz qual evento
		}
		if d.Supplier == "" || d.VID == "" || d.RemovedAt.IsZero() {
			return fmt.Errorf("%w: %s sem supplier, vid ou removed_at", messaging.ErrPoison, e.EventType)
		}
		return Catalog(tx, opts, log).ApplyRemoval(ctx, d.Supplier, d.VID, d.RemovedAt.Time) //nolint:wrapcheck // idem
	})
}
