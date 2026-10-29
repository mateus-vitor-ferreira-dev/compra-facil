// Comando sim sobe o emissor de NF-e simulado, no formato da API da Nuvem Fiscal. É dublê de teste:
// estado só em memória, comportamento determinístico e configurável; não vai para a produção.
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

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/invoice-sim/internal/config"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/invoice-sim/internal/handler"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/invoice-sim/internal/nfe"
)

func main() {
	// "sim healthcheck": o HEALTHCHECK do container (a imagem distroless não tem curl)
	if len(os.Args) > 1 && os.Args[1] == "healthcheck" {
		os.Exit(healthcheck())
	}
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, "invoice-sim:", err)
		os.Exit(1)
	}
}

func healthcheck() int {
	cfg, err := config.Load()
	if err != nil {
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
		return 1
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
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
		With("service", "invoice-sim")
	slog.SetDefault(log)
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	issuer := nfe.NewIssuer(nfe.Options{AllowedCFOP: cfg.AllowedCFOP, CancelWindow: cfg.CancelWindow})
	srv := &http.Server{
		Addr: fmt.Sprintf(":%d", cfg.Port),
		Handler: handler.NewRouter(issuer, handler.Credentials{
			ClientID: cfg.ClientID, ClientSecret: cfg.ClientSecret, TokenTTL: cfg.TokenTTL,
		}, log),
		ReadHeaderTimeout: 5 * time.Second,
		ReadTimeout:       10 * time.Second,
		WriteTimeout:      10 * time.Second,
	}
	errCh := make(chan error, 1)
	go func() {
		log.Info("simulador no ar", "addr", srv.Addr, "cfop", strings.Join(cfg.AllowedCFOP, ","))
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
	shutdownCtx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	if err := srv.Shutdown(shutdownCtx); err != nil {
		return fmt.Errorf("shutdown: %w", err)
	}
	return nil
}

func parseLevel(s string) slog.Level {
	switch strings.ToLower(s) {
	case "debug":
		return slog.LevelDebug
	case "warn":
		return slog.LevelWarn
	case "error":
		return slog.LevelError
	}
	return slog.LevelInfo
}
