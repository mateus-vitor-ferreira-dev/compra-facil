// Package grpcserver implementa catalog.v1.CatalogService (contracts/proto) sobre os services.
package grpcserver

import (
	"context"
	"errors"
	"fmt"
	"log/slog"

	"github.com/google/uuid"
	"github.com/grpc-ecosystem/go-grpc-middleware/v2/interceptors/logging"
	"github.com/grpc-ecosystem/go-grpc-middleware/v2/interceptors/recovery"
	"go.opentelemetry.io/contrib/instrumentation/google.golang.org/grpc/otelgrpc"
	"go.opentelemetry.io/contrib/instrumentation/google.golang.org/grpc/otelgrpc/filters"
	"google.golang.org/genproto/googleapis/rpc/errdetails"
	"google.golang.org/grpc"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/health"
	healthpb "google.golang.org/grpc/health/grpc_health_v1"
	"google.golang.org/grpc/reflection"
	"google.golang.org/grpc/status"

	catalogv1 "github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/gen/catalog/v1"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/service"
)

// maxBatch é o limite de BatchGetProducts (mesmo do service).
const maxBatch = 100

type products interface {
	AdminGet(ctx context.Context, id uuid.UUID) (domain.Product, error)
	GetMany(ctx context.Context, ids []uuid.UUID) ([]domain.Product, error)
	ValidateItems(ctx context.Context, items []service.ItemToValidate) (service.ItemsValidation, error)
}

type Server struct {
	catalogv1.UnimplementedCatalogServiceServer
	products products
	log      *slog.Logger
}

// Options controla o que é exposto além do serviço.
type Options struct {
	// Reflection habilita a reflexão (grpcurl sem o .proto). Ligada em dev.
	Reflection bool
}

// New monta o *grpc.Server com interceptors de log e recover, health check e o CatalogService.
// O *health.Server devolvido permite marcar NOT_SERVING no desligamento.
func New(p products, log *slog.Logger, opts Options) (*grpc.Server, *health.Server) {
	logOpts := []logging.Option{logging.WithLogOnEvents(logging.FinishCall)}
	recoverOpts := []recovery.Option{recovery.WithRecoveryHandlerContext(func(ctx context.Context, p any) error {
		log.ErrorContext(ctx, "panic recuperado no gRPC", slog.Any("panic", p))
		return status.Error(codes.Internal, "erro interno")
	})}

	srv := grpc.NewServer(
		// traces de cada chamada (o contexto vem do orders no header traceparent); health check fora
		grpc.StatsHandler(otelgrpc.NewServerHandler(otelgrpc.WithFilter(filters.Not(filters.HealthCheck())))),
		grpc.ChainUnaryInterceptor(
			logging.UnaryServerInterceptor(slogAdapter(log), logOpts...),
			recovery.UnaryServerInterceptor(recoverOpts...),
		),
	)
	catalogv1.RegisterCatalogServiceServer(srv, &Server{products: p, log: log})

	hs := health.NewServer()
	hs.SetServingStatus("", healthpb.HealthCheckResponse_SERVING)
	hs.SetServingStatus(catalogv1.CatalogService_ServiceDesc.ServiceName, healthpb.HealthCheckResponse_SERVING)
	healthpb.RegisterHealthServer(srv, hs)

	if opts.Reflection {
		reflection.Register(srv)
	}
	return srv, hs
}

// slogAdapter liga o interceptor de logging do go-grpc-middleware ao slog do serviço.
func slogAdapter(log *slog.Logger) logging.Logger {
	return logging.LoggerFunc(func(ctx context.Context, lvl logging.Level, msg string, fields ...any) {
		log.Log(ctx, slog.Level(lvl), msg, fields...)
	})
}

func (s *Server) GetProduct(ctx context.Context, req *catalogv1.GetProductRequest) (*catalogv1.GetProductResponse, error) {
	id, err := parseID("product_id", req.GetProductId())
	if err != nil {
		return nil, err
	}
	p, err := s.products.AdminGet(ctx, id)
	if err != nil {
		return nil, s.toStatus(ctx, err)
	}
	return &catalogv1.GetProductResponse{Product: toProto(p)}, nil
}

func (s *Server) BatchGetProducts(ctx context.Context, req *catalogv1.BatchGetProductsRequest) (*catalogv1.BatchGetProductsResponse, error) {
	if len(req.GetProductIds()) > maxBatch {
		return nil, invalidArgument("product_ids", fmt.Sprintf("no máximo %d por chamada", maxBatch))
	}
	ids := make([]uuid.UUID, len(req.GetProductIds()))
	for i, raw := range req.GetProductIds() {
		id, err := parseID(fmt.Sprintf("product_ids[%d]", i), raw)
		if err != nil {
			return nil, err
		}
		ids[i] = id
	}

	found, err := s.products.GetMany(ctx, ids)
	if err != nil {
		return nil, s.toStatus(ctx, err)
	}

	resp := &catalogv1.BatchGetProductsResponse{Products: make([]*catalogv1.Product, len(found))}
	seen := make(map[uuid.UUID]bool, len(found))
	for i, p := range found {
		resp.Products[i] = toProto(p)
		seen[p.ID] = true
	}
	for i, id := range ids {
		if !seen[id] {
			resp.NotFoundIds = append(resp.NotFoundIds, req.GetProductIds()[i])
			seen[id] = true // não repete o mesmo id desconhecido
		}
	}
	return resp, nil
}

func (s *Server) ValidateItems(ctx context.Context, req *catalogv1.ValidateItemsRequest) (*catalogv1.ValidateItemsResponse, error) {
	items := make([]service.ItemToValidate, len(req.GetItems()))
	for i, it := range req.GetItems() {
		id, err := parseID(fmt.Sprintf("items[%d].product_id", i), it.GetProductId())
		if err != nil {
			return nil, err
		}
		items[i] = service.ItemToValidate{ProductID: id, Quantity: int(it.GetQuantity())}
		if it.ExpectedUnitPriceCents != nil {
			expected := it.GetExpectedUnitPriceCents()
			items[i].ExpectedUnitPriceCents = &expected
		}
	}

	result, err := s.products.ValidateItems(ctx, items)
	if err != nil {
		return nil, s.toStatus(ctx, err)
	}
	return toValidateResponse(result), nil
}

// toStatus traduz erros de domínio para códigos gRPC; o resto vira Internal sem expor detalhes.
func (s *Server) toStatus(ctx context.Context, err error) error {
	var ve *domain.ValidationError
	switch {
	case errors.As(err, &ve):
		return validationStatus(ve.Fields)
	case errors.Is(err, domain.ErrNotFound):
		return status.Error(codes.NotFound, "produto não encontrado")
	default:
		s.log.ErrorContext(ctx, "erro inesperado no gRPC", slog.String("error", err.Error()))
		return status.Error(codes.Internal, "erro interno")
	}
}

func parseID(field, raw string) (uuid.UUID, error) {
	id, err := uuid.Parse(raw)
	if err != nil {
		return uuid.Nil, invalidArgument(field, "deve ser um UUID")
	}
	return id, nil
}

func invalidArgument(field, msg string) error {
	return validationStatus([]domain.FieldError{{Field: field, Message: msg}})
}

// validationStatus devolve INVALID_ARGUMENT com errdetails.BadRequest (um FieldViolation por campo).
func validationStatus(fields []domain.FieldError) error {
	br := &errdetails.BadRequest{}
	for _, f := range fields {
		br.FieldViolations = append(br.FieldViolations, &errdetails.BadRequest_FieldViolation{
			Field: f.Field, Description: f.Message,
		})
	}
	st, err := status.New(codes.InvalidArgument, (&domain.ValidationError{Fields: fields}).Error()).WithDetails(br)
	if err != nil {
		return status.Error(codes.InvalidArgument, (&domain.ValidationError{Fields: fields}).Error())
	}
	return st.Err()
}
