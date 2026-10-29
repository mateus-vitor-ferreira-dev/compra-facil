// Comando api sobe o serviço de catálogo: REST (Gin) e gRPC interno no mesmo processo.
package main

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"net"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/api/openapi"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/auth"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/cache"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/config"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/events"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/grpcserver"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/handler"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/messaging"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/repository"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/service"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/supplierfeed"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/telemetry"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/migrations"
)

// version é preenchida no build da imagem (-ldflags "-X main.version=...").
var version = "dev"

// drainDelay dá tempo ao gateway de ver o /ready em 503 antes de pararmos de aceitar conexões.
const drainDelay = 2 * time.Second

func main() {
	// "api healthcheck": usado pelo HEALTHCHECK do container (a imagem distroless não tem curl)
	if len(os.Args) > 1 && os.Args[1] == "healthcheck" {
		os.Exit(healthcheck())
	}
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, "catalog:", err)
		os.Exit(1)
	}
}

// healthcheck consulta o /ready local e devolve 0 se o serviço está pronto.
func healthcheck() int {
	cfg, err := config.Load("./config", ".")
	if err != nil {
		fmt.Fprintln(os.Stderr, "healthcheck:", err)
		return 1
	}
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, fmt.Sprintf("http://127.0.0.1:%d/ready", cfg.HTTP.Port), nil)
	if err != nil {
		return 1
	}
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		fmt.Fprintln(os.Stderr, "healthcheck:", err)
		return 1
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		fmt.Fprintln(os.Stderr, "healthcheck: /ready respondeu", resp.StatusCode)
		return 1
	}
	return 0
}

func run() error {
	cfg, err := config.Load("./config", ".")
	if err != nil {
		return fmt.Errorf("config: %w", err)
	}
	// OpenTelemetry antes do logger: com OTLP ligado, o logger também exporta (Loki)
	shutdownTelemetry, err := telemetry.Setup(context.Background(), cfg.App.Name, version, cfg.App.Env)
	if err != nil {
		return fmt.Errorf("telemetria: %w", err)
	}
	defer func() {
		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		_ = shutdownTelemetry(ctx) // descarrega o que estiver no buffer
	}()
	log := telemetry.NewLogger(os.Stdout, cfg.Log.Level, cfg.App.Name, cfg.App.Env).With("version", version)
	slog.SetDefault(log)

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	if cfg.DB.MigrateOnStart {
		if err := migrations.Up(cfg.DB.DSN); err != nil {
			return fmt.Errorf("migrações: %w", err)
		}
		log.Info("migrações aplicadas")
	}
	db, err := repository.Open(cfg.DB, log)
	if err != nil {
		return fmt.Errorf("postgres: %w", err)
	}
	sqlDB, err := db.DB()
	if err != nil {
		return fmt.Errorf("postgres: %w", err)
	}
	defer sqlDB.Close()
	relayDB, err := repository.Untraced(db) // antes de instrumentar: o relay não gera traces
	if err != nil {
		return fmt.Errorf("postgres: %w", err)
	}
	if telemetry.Enabled() {
		if err := repository.Instrument(db); err != nil {
			return err //nolint:wrapcheck // já descreve o que falhou
		}
	}

	health := handler.NewHealth()
	health.AddCheck("postgres", repository.Ping(db))

	products := repository.NewProductRepository(db)
	categories := repository.NewCategoryRepository(db)
	campaigns := repository.NewCampaignRepository(db)
	productSvc := service.NewProductService(products, categories, time.Now)
	categorySvc := service.NewCategoryService(categories)
	campaignSvc := service.NewCampaignService(campaigns, time.Now)

	verifier, err := auth.NewVerifier(ctx, cfg.Auth.Options(), log)
	if err != nil {
		return fmt.Errorf("auth: %w", err)
	}
	requireAdmin := handler.RequireRole(verifier, auth.RoleAdmin, log)

	var catalogAPI *handler.CatalogHandler
	var adminAPI *handler.AdminHandler
	wrap := func(h http.Handler) http.Handler { return h }
	// as escritas que não passam pelos writers do cache (feed do fornecedor, reprecificação) o invalidam por aqui
	invalidate := func(context.Context) {}
	if cfg.Cache.Enabled {
		store := cache.NewRedisStore(cache.RedisOptions{
			Addr:         cfg.Cache.Redis.Addr,
			Password:     cfg.Cache.Redis.Password,
			DB:           cfg.Cache.Redis.DB,
			DialTimeout:  cfg.Cache.Redis.Timeout,
			ReadTimeout:  cfg.Cache.Redis.Timeout,
			WriteTimeout: cfg.Cache.Redis.Timeout,
		})
		defer store.Close()
		// fora do /ready de propósito: sem Redis o serviço segue atendendo pelo banco
		if err := store.Ping(ctx); err != nil {
			log.Warn("redis indisponível na subida, leituras vão direto ao banco", "addr", cfg.Cache.Redis.Addr, "error", err.Error())
		}
		cached, err := cache.NewCatalog(store, productSvc, categorySvc, campaignSvc,
			cache.Options{ProductTTL: cfg.Cache.ProductTTL, ListTTL: cfg.Cache.ListTTL}, time.Now, log)
		if err != nil {
			return fmt.Errorf("cache: %w", err)
		}
		catalogAPI = handler.NewCatalogHandler(cached, cached, cached, log)
		invalidate = cached.Invalidate
		adminAPI = handler.NewAdminHandler(cache.NewProductWriter(productSvc, cached),
			cache.NewCategoryWriter(categorySvc, cached), cache.NewCampaignWriter(campaignSvc, cached), requireAdmin, log)
		wrap = cache.HeaderMiddleware
	} else {
		catalogAPI = handler.NewCatalogHandler(productSvc, categorySvc, campaignSvc, log)
		adminAPI = handler.NewAdminHandler(productSvc, categorySvc, campaignSvc, requireAdmin, log)
	}

	// fora do /ready de propósito: sem Kafka as escritas seguem e os eventos esperam no outbox
	stopRelay := func() {}
	if cfg.Events.RelayEnabled {
		kafka, err := events.NewKafka(events.KafkaOptions{Brokers: cfg.Events.Brokers, Timeout: cfg.Events.PublishTimeout})
		if err != nil {
			return fmt.Errorf("kafka: %w", err)
		}
		defer kafka.Close()
		relay := events.NewRelay(repository.NewOutboxRepository(relayDB), kafka, events.RelayOptions{
			PollInterval: cfg.Events.PollInterval,
			BatchSize:    cfg.Events.BatchSize,
			MaxBackoff:   cfg.Events.MaxBackoff,
		}, log)
		relayCtx, cancelRelay := context.WithCancel(context.Background())
		relayDone := make(chan struct{})
		go func() {
			defer close(relayDone)
			log.Info("relay do outbox no ar", "brokers", cfg.Events.Brokers)
			relay.Run(relayCtx)
		}()
		stopRelay = func() { cancelRelay(); <-relayDone }
		defer stopRelay() // saída por erro: para o relay antes do kafka.Close
	} else {
		log.Warn("relay do outbox desligado: eventos ficam no outbox sem publicar")
	}

	// catálogo do fornecedor (#217): o feed vira produto pela regra de margem
	supplierOpts, err := supplierOptions(cfg.Supplier)
	if err != nil {
		return err
	}
	pricing := supplierfeed.Catalog(db, supplierOpts, log)
	var deadLetters handler.DeadLetterAdmin
	stopConsumer := func() {}
	if cfg.Supplier.ConsumerEnabled {
		consumer, err := messaging.NewConsumer(db, messaging.Options{
			Brokers: cfg.Events.Brokers, Service: "catalog", PollInterval: cfg.Events.PollInterval, MaxBackoff: cfg.Events.MaxBackoff,
		}, log)
		if err != nil {
			return fmt.Errorf("consumer: %w", err)
		}
		defer consumer.Close()
		supplierfeed.Register(consumer, supplierOpts, log)
		consumer.AfterCommit(func(ctx context.Context, _ messaging.Event) { invalidate(ctx) })
		deadLetters = consumer.DeadLetters()
		consumerCtx, cancelConsumer := context.WithCancel(context.Background())
		consumerDone := make(chan struct{})
		go func() {
			defer close(consumerDone)
			log.Info("consumo do feed do fornecedor no ar", "topic", supplierfeed.Topic)
			if err := consumer.Run(consumerCtx); err != nil {
				log.Error("consumo do feed parou", "error", err.Error())
			}
		}()
		stopConsumer = func() { cancelConsumer(); <-consumerDone }
		defer stopConsumer()
	} else {
		log.Warn("consumo do feed do fornecedor desligado")
	}
	supplierAPI := handler.NewSupplierAdminHandler(pricing, deadLetters, invalidate, requireAdmin, log)

	srv := &http.Server{
		Addr:              cfg.HTTP.Addr(),
		Handler:           wrap(handler.NewRouter(log, health, catalogAPI, adminAPI, supplierAPI, handler.NewDocs(openapi.Files))),
		ReadTimeout:       cfg.HTTP.ReadTimeout,
		ReadHeaderTimeout: cfg.HTTP.ReadTimeout,
		WriteTimeout:      cfg.HTTP.WriteTimeout,
	}
	grpcSrv, grpcHealth := grpcserver.New(productSvc, log, grpcserver.Options{Reflection: cfg.GRPC.Reflection})
	grpcLis, err := (&net.ListenConfig{}).Listen(ctx, "tcp", cfg.GRPC.Addr())
	if err != nil {
		return fmt.Errorf("gRPC: %w", err)
	}

	errCh := make(chan error, 2)
	go func() {
		log.Info("servidor HTTP no ar", "addr", srv.Addr)
		if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			errCh <- fmt.Errorf("servidor HTTP: %w", err)
		}
	}()
	go func() {
		log.Info("servidor gRPC no ar", "addr", cfg.GRPC.Addr(), "reflection", cfg.GRPC.Reflection)
		if err := grpcSrv.Serve(grpcLis); err != nil {
			errCh <- fmt.Errorf("servidor gRPC: %w", err)
		}
	}()

	select {
	case err := <-errCh:
		return err
	case <-ctx.Done():
	}
	stop() // um segundo Ctrl+C mata o processo na hora

	log.Info("desligando", "timeout", cfg.HTTP.ShutdownTimeout.String())
	health.MarkShuttingDown()
	grpcHealth.Shutdown() // health gRPC passa a NOT_SERVING
	if cfg.App.Env != "local" {
		time.Sleep(drainDelay)
	}

	shutdownCtx, cancel := context.WithTimeout(context.Background(), cfg.HTTP.ShutdownTimeout)
	defer cancel()
	go func() {
		<-shutdownCtx.Done()
		grpcSrv.Stop() // timeout: corta as chamadas que não terminaram
	}()
	grpcSrv.GracefulStop()
	if err := srv.Shutdown(shutdownCtx); err != nil {
		return fmt.Errorf("shutdown: %w", err)
	}
	// o consumer antes do relay: o que ele gravar ainda sai pelo outbox
	stopConsumer()
	// depois do HTTP, para não sobrar escrita sem relay; o que não saiu fica no outbox para o
	// próximo start (ou outra réplica)
	stopRelay()
	log.Info("desligado")
	return nil
}

// supplierOptions monta a configuração do catálogo do fornecedor (já validada no config.Load).
func supplierOptions(c config.SupplierConfig) (service.SupplierOptions, error) {
	categories, err := c.CategoryMap()
	if err != nil {
		return service.SupplierOptions{}, fmt.Errorf("config: %w", err)
	}
	usd, err := domain.ParseExchangeRate(c.USDBRL)
	if err != nil {
		return service.SupplierOptions{}, fmt.Errorf("config: %w", err)
	}
	return service.SupplierOptions{
		Rates:       domain.ExchangeRates{"USD": usd},
		Categories:  categories,
		DefaultRule: domain.PricingRule{MarginPercent: c.DefaultMarginPercent, MinPriceCents: c.DefaultMinPriceCents},
		PublishNew:  c.PublishNew,
	}, nil
}
