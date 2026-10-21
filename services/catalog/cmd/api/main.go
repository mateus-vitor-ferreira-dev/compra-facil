// Comando api sobe o serviço de catálogo (HTTP; gRPC entra na tarefa #66).
package main

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/config"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/handler"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/repository"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/telemetry"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/migrations"
)

// drainDelay dá tempo ao gateway de ver o /ready em 503 antes de pararmos de aceitar conexões.
const drainDelay = 2 * time.Second

func main() {
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, "catalog:", err)
		os.Exit(1)
	}
}

func run() error {
	cfg, err := config.Load("./config", ".")
	if err != nil {
		return fmt.Errorf("config: %w", err)
	}
	log := telemetry.NewLogger(os.Stdout, cfg.Log.Level, cfg.App.Name, cfg.App.Env)
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
	srv := &http.Server{
		Addr:              cfg.HTTP.Addr(),
		Handler:           handler.NewRouter(log, health),
		ReadTimeout:       cfg.HTTP.ReadTimeout,
		ReadHeaderTimeout: cfg.HTTP.ReadTimeout,
		WriteTimeout:      cfg.HTTP.WriteTimeout,
	}

	errCh := make(chan error, 1)
	go func() {
		log.Info("servidor HTTP no ar", "addr", srv.Addr)
		if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			errCh <- err
		}
		close(errCh)
	}()

	select {
	case err := <-errCh:
		return fmt.Errorf("servidor HTTP: %w", err)
	case <-ctx.Done():
	}
	stop() // um segundo Ctrl+C mata o processo na hora

	log.Info("desligando", "timeout", cfg.HTTP.ShutdownTimeout.String())
	health.MarkShuttingDown()
	if cfg.App.Env != "local" {
		time.Sleep(drainDelay)
	}

	shutdownCtx, cancel := context.WithTimeout(context.Background(), cfg.HTTP.ShutdownTimeout)
	defer cancel()
	if err := srv.Shutdown(shutdownCtx); err != nil {
		return fmt.Errorf("shutdown: %w", err)
	}
	log.Info("desligado")
	return nil
}
