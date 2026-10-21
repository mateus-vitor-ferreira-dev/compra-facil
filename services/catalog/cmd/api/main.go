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

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/api/openapi"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/auth"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/config"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/grpcserver"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/handler"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/repository"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/service"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/telemetry"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/migrations"
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

	health := handler.NewHealth()
	health.AddCheck("postgres", repository.Ping(db))

	products := repository.NewProductRepository(db)
	categories := repository.NewCategoryRepository(db)
	campaigns := repository.NewCampaignRepository(db)
	productSvc := service.NewProductService(products, categories, time.Now)
	categorySvc := service.NewCategoryService(categories)
	campaignSvc := service.NewCampaignService(campaigns, time.Now)
	catalogAPI := handler.NewCatalogHandler(productSvc, categorySvc, campaignSvc, log)

	verifier, err := auth.NewVerifier(cfg.Auth.Options())
	if err != nil {
		return fmt.Errorf("auth: %w", err)
	}
	adminAPI := handler.NewAdminHandler(productSvc, categorySvc, campaignSvc,
		handler.RequireRole(verifier, auth.RoleAdmin, log), log)

	srv := &http.Server{
		Addr:              cfg.HTTP.Addr(),
		Handler:           handler.NewRouter(log, health, catalogAPI, adminAPI, handler.NewDocs(openapi.Files)),
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
	log.Info("desligado")
	return nil
}
