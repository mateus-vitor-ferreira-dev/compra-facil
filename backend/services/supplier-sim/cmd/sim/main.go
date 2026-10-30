// Comando sim sobe o fornecedor simulado de dropshipping, no formato da API v2 da CJ.
// É dublê de teste: estado só em memória, comportamento determinístico e configurável.
package main

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/catalog"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/config"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/handler"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/order"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/webhook"
)

func main() {
	// "sim healthcheck": usado pelo HEALTHCHECK do container (a imagem distroless não tem curl)
	if len(os.Args) > 1 && os.Args[1] == "healthcheck" {
		os.Exit(healthcheck())
	}
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, "supplier-sim:", err)
		os.Exit(1)
	}
}

// healthcheck consulta o /health local e devolve 0 se o simulador responde.
func healthcheck() int {
	cfg, err := config.Load()
	if err != nil {
		fmt.Fprintln(os.Stderr, "healthcheck:", err)
		return 1
	}
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, fmt.Sprintf("http://127.0.0.1:%d/health", cfg.Port), nil)
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
		fmt.Fprintln(os.Stderr, "healthcheck: /health respondeu", resp.StatusCode)
		return 1
	}
	return 0
}

func run() error {
	cfg, err := config.Load()
	if err != nil {
		return fmt.Errorf("config: %w", err)
	}
	log := slog.New(slog.NewJSONHandler(os.Stdout, &slog.HandlerOptions{Level: parseLevel(cfg.LogLevel)})).
		With("service", "supplier-sim")
	slog.SetDefault(log)

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	hooks := webhook.NewDispatcher(webhook.Options{
		URL:       cfg.WebhookURL,
		Secret:    cfg.WebhookSecret,
		Attempts:  cfg.WebhookAttempts,
		RetryBase: cfg.WebhookRetryBase,
		Duplicate: cfg.WebhookDuplicate,
	}, log)
	if cfg.WebhookURL == "" {
		log.Warn("SIM_WEBHOOK_URL vazia: mudanças de status não serão notificadas")
	}
	stock := catalog.NewStock(cfg.OutOfStock)
	orders := order.NewSimulator(stock, order.Delays{Accept: cfg.AcceptDelay, Ship: cfg.ShipDelay, Deliver: cfg.DeliverDelay, Dispute: cfg.DisputeDelay}, hooks)
	tokens := handler.NewTokens(cfg.Email, cfg.Password, cfg.TokenTTL)

	srv := &http.Server{
		Addr:              fmt.Sprintf(":%d", cfg.Port),
		Handler:           handler.NewRouter(tokens, stock, orders, hooks, log),
		ReadHeaderTimeout: 5 * time.Second,
		ReadTimeout:       10 * time.Second,
		WriteTimeout:      10 * time.Second,
	}
	errCh := make(chan error, 1)
	go func() {
		log.Info("simulador no ar", "addr", srv.Addr, "webhook_url", cfg.WebhookURL,
			"accept_delay", cfg.AcceptDelay.String(), "ship_delay", cfg.ShipDelay.String(),
			"deliver_delay", cfg.DeliverDelay.String())
		if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			errCh <- fmt.Errorf("servidor HTTP: %w", err)
		}
	}()

	select {
	case err := <-errCh:
		return err
	case <-ctx.Done():
	}
	stop()

	log.Info("desligando")
	shutdownCtx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	if err := srv.Shutdown(shutdownCtx); err != nil {
		return fmt.Errorf("shutdown: %w", err)
	}
	// ciclos de vida antes dos webhooks: assim nenhum passo novo entra numa fila já fechada
	orders.Close()
	hooks.Close()
	log.Info("desligado")
	return nil
}

func parseLevel(level string) slog.Level {
	switch strings.ToLower(level) {
	case "debug":
		return slog.LevelDebug
	case "warn":
		return slog.LevelWarn
	case "error":
		return slog.LevelError
	default:
		return slog.LevelInfo
	}
}
