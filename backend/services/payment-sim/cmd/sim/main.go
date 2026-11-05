// Comando sim sobe o provedor de pagamento simulado, no formato da API do Mercado Pago. É dublê de teste:
// estado só em memória, comportamento determinístico e configurável; não vai para a produção.
package main

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"errors"
	"fmt"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/payment-sim/internal/config"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/payment-sim/internal/efi"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/payment-sim/internal/handler"
)

func main() {
	// "sim healthcheck": o HEALTHCHECK do container (a imagem distroless não tem curl)
	if len(os.Args) > 1 && os.Args[1] == "healthcheck" {
		os.Exit(healthcheck())
	}
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, "payment-sim:", err)
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
		With("service", "payment-sim")
	slog.SetDefault(log)
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	efiHooks := efi.NewDispatcher(efi.DispatcherOptions{RetryBase: cfg.WebhookRetryBase}, log)
	efiProvider := efi.NewProvider(efi.Options{
		ClientID: cfg.EfiClientID, ClientSecret: cfg.EfiClientSecret, PayeeCode: cfg.EfiPayeeCode, PixKey: cfg.EfiPixKey,
		SettleDelay: cfg.EfiSettleDelay, RefundDelay: cfg.EfiRefundDelay,
		PayPageURL: func(token string) string { return cfg.PublicURL + "/sim/cob/pagar/" + token },
	}, efiHooks)

	srv := &http.Server{
		Addr:              fmt.Sprintf(":%d", cfg.Port),
		Handler:           handler.NewRouter(efiProvider, handler.Credentials{AdminToken: adminToken(cfg.AdminToken, log)}, log),
		ReadHeaderTimeout: 5 * time.Second,
		ReadTimeout:       10 * time.Second,
		WriteTimeout:      10 * time.Second,
	}
	errCh := make(chan error, 1)
	go func() {
		log.Info("simulador no ar", "addr", srv.Addr, "settle_delay", cfg.EfiSettleDelay.String())
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
	efiProvider.Close()
	efiHooks.Close()
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

// adminToken é o SIM_ADMIN_TOKEN ou, sem ele, um aleatório que ninguém conhece: as rotas /sim de operação ficam
// fechadas, como na demonstração pública (#280). O token gerado não vai para o log.
func adminToken(configured string, log *slog.Logger) string {
	if configured != "" {
		return configured
	}
	b := make([]byte, 32)
	if _, err := rand.Read(b); err != nil {
		panic(err)
	}
	log.Info("SIM_ADMIN_TOKEN vazio: as rotas /sim de operação ficam fechadas")
	return hex.EncodeToString(b)
}
