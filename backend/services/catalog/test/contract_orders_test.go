package test

import (
	"context"
	"encoding/json"
	"fmt"
	"log/slog"
	"net"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials/insecure"
	"google.golang.org/grpc/test/bufconn"
	"gorm.io/gorm"

	catalogv1 "github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/gen/catalog/v1"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/grpcserver"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/repository"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/service"
)

// Contrato consumer-driven escrito pelo orders (ADR 0004). Este teste é o lado do provedor: cada
// interação vira um estado no banco real e uma chamada ao ValidateItems pelo servidor gRPC real.
// Falhou: o catalog quebrou algo de que o orders depende, e a imagem não é publicada (catalog.yml).
const consumerContract = "../../../../contracts/consumer/orders-catalog.json"

const (
	anyString = "<string>"
	anyUUID   = "<uuid>"
)

type contractMoney struct {
	AmountCents int64  `json:"amount_cents"`
	Currency    string `json:"currency"`
}

type contractInteraction struct {
	Description string `json:"description"`
	Given       map[string]struct {
		Status                  string `json:"status"`
		PriceCents              int64  `json:"price_cents"`
		CampaignDiscountPercent *int   `json:"campaign_discount_percent"`
	} `json:"given"`
	Request []struct {
		Product                string `json:"product"`
		Quantity               int32  `json:"quantity"`
		ExpectedUnitPriceCents *int64 `json:"expected_unit_price_cents"`
	} `json:"request"`
	Response []struct {
		Product    string         `json:"product"`
		Status     string         `json:"status"`
		Quantity   *int32         `json:"quantity"`
		Sku        *string        `json:"sku"`
		Name       *string        `json:"name"`
		UnitPrice  *contractMoney `json:"unit_price"`
		CampaignID *string        `json:"campaign_id"`
	} `json:"response"`
}

func TestConsumerContract_OrdersValidateItems(t *testing.T) {
	if testing.Short() {
		t.Skip("integração (Postgres): verificação do contrato do orders")
	}
	raw, err := os.ReadFile(filepath.Clean(consumerContract))
	require.NoError(t, err, "contrato do orders não encontrado")
	var contract struct {
		Consumer     string                `json:"consumer"`
		Provider     string                `json:"provider"`
		Interactions []contractInteraction `json:"interactions"`
	}
	require.NoError(t, json.Unmarshal(raw, &contract))
	require.Equal(t, "catalog", contract.Provider)
	require.NotEmpty(t, contract.Interactions)

	log := slog.New(slog.NewTextHandler(os.Stderr, &slog.HandlerOptions{Level: slog.LevelWarn}))
	db := startPostgres(t, log)
	client := grpcClient(t, db, log)

	for i, it := range contract.Interactions {
		t.Run(it.Description, func(t *testing.T) {
			ids := givenState(t, db, i, it)
			req := &catalogv1.ValidateItemsRequest{}
			for _, r := range it.Request {
				req.Items = append(req.Items, &catalogv1.ItemToValidate{
					ProductId: idOf(ids, r.Product).String(), Quantity: r.Quantity, ExpectedUnitPriceCents: r.ExpectedUnitPriceCents,
				})
			}

			resp, err := client.ValidateItems(t.Context(), req)
			require.NoError(t, err)

			require.Len(t, resp.GetItems(), len(it.Response), "um item por produto do request")
			byID := map[string]*catalogv1.ValidatedItem{}
			for _, item := range resp.GetItems() {
				byID[item.GetProductId()] = item
			}
			for _, want := range it.Response {
				got, ok := byID[idOf(ids, want.Product).String()]
				require.True(t, ok, "resposta sem o produto %s", want.Product)
				assert.Equal(t, want.Status, got.GetStatus().String(), "%s: status", want.Product)
				if want.Quantity != nil {
					assert.Equal(t, *want.Quantity, got.GetQuantity(), "%s: quantity", want.Product)
				}
				matchString(t, want.Sku, got.GetSku(), want.Product+": sku")
				matchString(t, want.Name, got.GetName(), want.Product+": name")
				matchString(t, want.CampaignID, got.GetCampaignId(), want.Product+": campaign_id")
				if want.UnitPrice != nil {
					require.NotNil(t, got.GetUnitPrice(), "%s: unit_price", want.Product)
					assert.Equal(t, want.UnitPrice.AmountCents, got.GetUnitPrice().GetAmountCents(), "%s: unit_price.amount_cents", want.Product)
					assert.Equal(t, want.UnitPrice.Currency, got.GetUnitPrice().GetCurrency(), "%s: unit_price.currency", want.Product)
				}
			}
		})
	}
}

// givenState cria os produtos (e campanhas) da interação; produto citado e ausente de given fica
// sem registro, ou seja, não existe.
func givenState(t *testing.T, db *gorm.DB, n int, it contractInteraction) map[string]uuid.UUID {
	t.Helper()
	ctx := context.Background()
	cat := domain.Category{Slug: fmt.Sprintf("contrato-%d", n), Name: fmt.Sprintf("Contrato %d", n)}
	require.NoError(t, repository.NewCategoryRepository(db).Create(ctx, &cat))
	products := repository.NewProductRepository(db)
	campaigns := repository.NewCampaignRepository(db)

	ids := map[string]uuid.UUID{}
	for ref, g := range it.Given {
		p := domain.Product{
			SKU: fmt.Sprintf("CT-%d-%s", n, ref), Name: "Produto do contrato " + ref, CategoryID: cat.ID,
			PriceCents: g.PriceCents, Status: domain.ProductStatus(g.Status),
		}
		require.NoError(t, products.Create(ctx, &p))
		ids[ref] = p.ID
		if g.CampaignDiscountPercent != nil {
			c := domain.Campaign{
				Slug: fmt.Sprintf("contrato-%d-%s", n, ref), Name: "Campanha do contrato", DiscountPercent: *g.CampaignDiscountPercent,
				StartsAt: time.Now().Add(-time.Hour), EndsAt: time.Now().Add(time.Hour),
			}
			require.NoError(t, campaigns.Create(ctx, &c))
			require.NoError(t, campaigns.SetProducts(ctx, c.ID, []uuid.UUID{p.ID}))
		}
	}
	return ids
}

// idOf devolve o id do produto criado, ou um id novo (que não existe) para a referência sem given.
func idOf(ids map[string]uuid.UUID, ref string) uuid.UUID {
	if id, ok := ids[ref]; ok {
		return id
	}
	id := uuid.NewSHA1(uuid.NameSpaceOID, []byte("inexistente:"+ref))
	ids[ref] = id
	return id
}

func matchString(t *testing.T, want *string, got, field string) {
	t.Helper()
	switch {
	case want == nil:
	case *want == anyString:
		assert.NotEmpty(t, got, field)
	case *want == anyUUID:
		assert.Regexp(t, `^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$`, got, field)
	default:
		assert.Equal(t, *want, got, field)
	}
}

// grpcClient sobe o servidor gRPC real do catalog (o mesmo do main) num listener em memória.
func grpcClient(t *testing.T, db *gorm.DB, log *slog.Logger) catalogv1.CatalogServiceClient {
	t.Helper()
	productSvc := service.NewProductService(repository.NewProductRepository(db), repository.NewCategoryRepository(db), time.Now)
	srv, _ := grpcserver.New(productSvc, log, grpcserver.Options{})
	lis := bufconn.Listen(1 << 20)
	go func() { _ = srv.Serve(lis) }()
	t.Cleanup(srv.Stop)

	conn, err := grpc.NewClient("passthrough:///bufnet",
		grpc.WithContextDialer(func(ctx context.Context, _ string) (net.Conn, error) { return lis.DialContext(ctx) }),
		grpc.WithTransportCredentials(insecure.NewCredentials()),
	)
	require.NoError(t, err)
	t.Cleanup(func() { _ = conn.Close() })
	return catalogv1.NewCatalogServiceClient(conn)
}
