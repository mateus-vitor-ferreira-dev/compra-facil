package grpcserver

import (
	"context"
	"errors"
	"log/slog"
	"net"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"google.golang.org/genproto/googleapis/rpc/errdetails"
	"google.golang.org/grpc"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/credentials/insecure"
	healthpb "google.golang.org/grpc/health/grpc_health_v1"
	"google.golang.org/grpc/status"
	"google.golang.org/grpc/test/bufconn"
	"google.golang.org/protobuf/proto"

	catalogv1 "github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/gen/catalog/v1"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/service"
)

type fakeProducts struct {
	product    domain.Product
	many       []domain.Product
	validation service.ItemsValidation
	err        error
	panicOn    string
	gotItems   []service.ItemToValidate
}

func (f *fakeProducts) AdminGet(context.Context, uuid.UUID) (domain.Product, error) {
	if f.panicOn == "get" {
		panic("bug")
	}
	return f.product, f.err
}

func (f *fakeProducts) GetMany(context.Context, []uuid.UUID) ([]domain.Product, error) {
	return f.many, f.err
}

func (f *fakeProducts) ValidateItems(_ context.Context, items []service.ItemToValidate) (service.ItemsValidation, error) {
	f.gotItems = items
	return f.validation, f.err
}

// dial sobe o servidor real (interceptors, health, reflection) num listener em memória.
func dial(t *testing.T, p products) (catalogv1.CatalogServiceClient, healthpb.HealthClient) {
	t.Helper()
	lis := bufconn.Listen(1 << 20)
	srv, _ := New(p, slog.New(slog.DiscardHandler), Options{Reflection: true})
	go func() { _ = srv.Serve(lis) }()
	t.Cleanup(srv.Stop)

	conn, err := grpc.NewClient("passthrough:///bufnet",
		grpc.WithContextDialer(func(ctx context.Context, _ string) (net.Conn, error) { return lis.DialContext(ctx) }),
		grpc.WithTransportCredentials(insecure.NewCredentials()),
	)
	require.NoError(t, err)
	t.Cleanup(func() { _ = conn.Close() })
	return catalogv1.NewCatalogServiceClient(conn), healthpb.NewHealthClient(conn)
}

var ctx = context.Background()

func sampleProduct() domain.Product {
	return domain.Product{
		ID: uuid.New(), SKU: "TV-55", Name: "Smart TV", CategoryID: uuid.New(), PriceCents: 10000, Currency: "BRL",
		Status: domain.ProductActive, Version: 2, UpdatedAt: time.Date(2026, 11, 1, 0, 0, 0, 0, time.UTC),
		ActiveCampaign: &domain.CampaignRef{ID: uuid.New(), Name: "BF", DiscountPercent: 30, EndsAt: time.Date(2026, 11, 28, 0, 0, 0, 0, time.UTC)},
	}
}

func requireCode(t *testing.T, err error, want codes.Code) *status.Status {
	t.Helper()
	st, ok := status.FromError(err)
	require.True(t, ok, "erro gRPC esperado, veio %v", err)
	require.Equal(t, want, st.Code(), st.Message())
	return st
}

func TestGetProduct(t *testing.T) {
	p := sampleProduct()
	client, _ := dial(t, &fakeProducts{product: p})

	resp, err := client.GetProduct(ctx, &catalogv1.GetProductRequest{ProductId: p.ID.String()})

	require.NoError(t, err)
	got := resp.GetProduct()
	assert.Equal(t, "TV-55", got.GetSku())
	assert.Equal(t, catalogv1.ProductStatus_PRODUCT_STATUS_ACTIVE, got.GetStatus())
	assert.Equal(t, int64(10000), got.GetPrice().GetAmountCents())
	assert.Equal(t, int64(7000), got.GetEffectivePrice().GetAmountCents())
	assert.Equal(t, int32(30), got.GetCampaign().GetDiscountPercent())
	assert.Equal(t, int32(2), got.GetVersion())
	assert.Equal(t, p.CategoryID.String(), got.GetCategoryId())
}

func TestGetProduct_Errors(t *testing.T) {
	t.Run("id inválido: INVALID_ARGUMENT com o campo", func(t *testing.T) {
		client, _ := dial(t, &fakeProducts{})
		_, err := client.GetProduct(ctx, &catalogv1.GetProductRequest{ProductId: "123"})

		st := requireCode(t, err, codes.InvalidArgument)
		require.Len(t, st.Details(), 1)
		br := st.Details()[0].(*errdetails.BadRequest)
		assert.Equal(t, "product_id", br.GetFieldViolations()[0].GetField())
	})
	t.Run("inexistente: NOT_FOUND", func(t *testing.T) {
		client, _ := dial(t, &fakeProducts{err: domain.ErrNotFound})
		_, err := client.GetProduct(ctx, &catalogv1.GetProductRequest{ProductId: uuid.NewString()})
		requireCode(t, err, codes.NotFound)
	})
	t.Run("erro inesperado: INTERNAL sem detalhe", func(t *testing.T) {
		client, _ := dial(t, &fakeProducts{err: errors.New("senha: hunter2")})
		_, err := client.GetProduct(ctx, &catalogv1.GetProductRequest{ProductId: uuid.NewString()})
		st := requireCode(t, err, codes.Internal)
		assert.NotContains(t, st.Message(), "hunter2")
	})
	t.Run("panic: INTERNAL e o servidor continua de pé", func(t *testing.T) {
		f := &fakeProducts{panicOn: "get"}
		client, _ := dial(t, f)
		_, err := client.GetProduct(ctx, &catalogv1.GetProductRequest{ProductId: uuid.NewString()})
		requireCode(t, err, codes.Internal)

		f.panicOn = ""
		_, err = client.GetProduct(ctx, &catalogv1.GetProductRequest{ProductId: uuid.NewString()})
		require.NoError(t, err)
	})
}

func TestBatchGetProducts(t *testing.T) {
	p := sampleProduct()
	unknown := uuid.NewString()
	client, _ := dial(t, &fakeProducts{many: []domain.Product{p}})

	resp, err := client.BatchGetProducts(ctx, &catalogv1.BatchGetProductsRequest{
		ProductIds: []string{p.ID.String(), unknown, unknown},
	})

	require.NoError(t, err)
	require.Len(t, resp.GetProducts(), 1)
	assert.Equal(t, []string{unknown}, resp.GetNotFoundIds(), "id desconhecido aparece uma vez")

	_, err = client.BatchGetProducts(ctx, &catalogv1.BatchGetProductsRequest{ProductIds: make([]string, 101)})
	requireCode(t, err, codes.InvalidArgument)

	_, err = client.BatchGetProducts(ctx, &catalogv1.BatchGetProductsRequest{ProductIds: []string{"x"}})
	requireCode(t, err, codes.InvalidArgument)
}

func TestValidateItems(t *testing.T) {
	p := sampleProduct()
	missing := uuid.New()
	pricedAt := time.Date(2026, 11, 27, 12, 0, 0, 0, time.UTC)
	f := &fakeProducts{validation: service.ItemsValidation{
		Valid: false, Currency: "BRL", TotalCents: 14000, PricedAt: pricedAt,
		Items: []service.ValidatedItem{
			{ProductID: p.ID, Status: service.ItemOK, Quantity: 2, Product: &p, UnitPriceCents: 7000, LineTotalCents: 14000},
			{ProductID: missing, Status: service.ItemNotFound, Quantity: 1},
		},
	}}
	client, _ := dial(t, f)

	resp, err := client.ValidateItems(ctx, &catalogv1.ValidateItemsRequest{Items: []*catalogv1.ItemToValidate{
		{ProductId: p.ID.String(), Quantity: 2, ExpectedUnitPriceCents: proto.Int64(7000)},
		{ProductId: missing.String(), Quantity: 1},
	}})

	require.NoError(t, err)
	assert.Equal(t, []service.ItemToValidate{
		{ProductID: p.ID, Quantity: 2, ExpectedUnitPriceCents: proto.Int64(7000)},
		{ProductID: missing, Quantity: 1},
	}, f.gotItems, "preço esperado ausente continua nil")

	assert.False(t, resp.GetValid())
	assert.Equal(t, int64(14000), resp.GetTotal().GetAmountCents())
	assert.Equal(t, pricedAt, resp.GetPricedAt().AsTime())
	ok, notFound := resp.GetItems()[0], resp.GetItems()[1]
	assert.Equal(t, catalogv1.ItemStatus_ITEM_STATUS_OK, ok.GetStatus())
	assert.Equal(t, "TV-55", ok.GetSku())
	assert.Equal(t, int64(7000), ok.GetUnitPrice().GetAmountCents())
	assert.Equal(t, p.ActiveCampaign.ID.String(), ok.GetCampaignId())
	assert.Equal(t, catalogv1.ItemStatus_ITEM_STATUS_NOT_FOUND, notFound.GetStatus())
	assert.Nil(t, notFound.GetUnitPrice())
}

func TestValidateItems_Errors(t *testing.T) {
	t.Run("id inválido aponta o item", func(t *testing.T) {
		client, _ := dial(t, &fakeProducts{})
		_, err := client.ValidateItems(ctx, &catalogv1.ValidateItemsRequest{Items: []*catalogv1.ItemToValidate{
			{ProductId: uuid.NewString(), Quantity: 1}, {ProductId: "nope", Quantity: 1},
		}})
		st := requireCode(t, err, codes.InvalidArgument)
		assert.Equal(t, "items[1].product_id", st.Details()[0].(*errdetails.BadRequest).GetFieldViolations()[0].GetField())
	})
	t.Run("validação do service vira INVALID_ARGUMENT", func(t *testing.T) {
		client, _ := dial(t, &fakeProducts{err: domain.Invalid("items", "vazio")})
		_, err := client.ValidateItems(ctx, &catalogv1.ValidateItemsRequest{})
		requireCode(t, err, codes.InvalidArgument)
	})
}

func TestHealth(t *testing.T) {
	_, hc := dial(t, &fakeProducts{})
	for _, svc := range []string{"", catalogv1.CatalogService_ServiceDesc.ServiceName} {
		resp, err := hc.Check(ctx, &healthpb.HealthCheckRequest{Service: svc})
		require.NoError(t, err)
		assert.Equal(t, healthpb.HealthCheckResponse_SERVING, resp.GetStatus(), svc)
	}
}
