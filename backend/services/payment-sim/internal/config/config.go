// Package config lê a configuração do simulador só de variáveis de ambiente.
package config

import (
	"fmt"
	"os"
	"strconv"
	"strings"
	"time"
)

type Config struct {
	Port     int
	LogLevel string

	AccessToken string
	PublicKey   string
	// AdminToken abre as rotas /sim de operação; vazio, o simulador gera um aleatório ao subir, e elas ficam
	// fechadas (a demonstração pública não precisa delas)
	AdminToken string

	PIXExpiration time.Duration
	ReviewDelay   time.Duration
	// PublicURL é de onde o navegador acessa o simulador (o ticket_url do PIX aponta para lá)
	PublicURL string

	WebhookURL       string
	WebhookSecret    string
	WebhookRetryBase time.Duration

	// a Efí (#379): as credenciais da aplicação, o identificador de conta (vai no navegador) e a chave PIX
	EfiClientID     string
	EfiClientSecret string
	EfiPayeeCode    string
	EfiPixKey       string
	// EfiSettleDelay: quanto o cartão aprovado leva para ficar pago (só então pode ser estornado)
	EfiSettleDelay time.Duration
	EfiRefundDelay time.Duration
}

// Load lê o ambiente aplicando os padrões; valor inválido é erro, não padrão silencioso.
func Load() (Config, error) { return load(os.Getenv) }

func load(getenv func(string) string) (Config, error) {
	c := Config{
		LogLevel:      str(getenv, "LOG_LEVEL", "info"),
		AccessToken:   str(getenv, "SIM_ACCESS_TOKEN", "TEST-sim-access-token"),
		PublicKey:     str(getenv, "SIM_PUBLIC_KEY", "TEST-sim-public-key"),
		AdminToken:    strings.TrimSpace(getenv("SIM_ADMIN_TOKEN")),
		PublicURL:     strings.TrimRight(str(getenv, "SIM_PUBLIC_URL", "http://localhost:8092"), "/"),
		WebhookURL:    strings.TrimSpace(getenv("SIM_WEBHOOK_URL")),
		WebhookSecret: str(getenv, "SIM_WEBHOOK_SECRET", "sim-secret"),

		EfiClientID:     str(getenv, "SIM_EFI_CLIENT_ID", "Client_Id_sim"),
		EfiClientSecret: str(getenv, "SIM_EFI_CLIENT_SECRET", "Client_Secret_sim"),
		EfiPayeeCode:    str(getenv, "SIM_EFI_PAYEE_CODE", "0123456789abcdef0123456789abcdef"),
		EfiPixKey:       str(getenv, "SIM_EFI_PIX_KEY", "7d9f0335-8dcc-4054-9bf9-0dbd61d36906"),
	}
	var err error
	if c.Port, err = intVal(getenv, "PORT", 8090); err != nil {
		return c, err
	}
	for _, d := range []struct {
		dst  *time.Duration
		name string
		def  time.Duration
	}{
		{&c.PIXExpiration, "SIM_PIX_EXPIRATION", 30 * time.Minute},
		{&c.ReviewDelay, "SIM_REVIEW_DELAY", 5 * time.Second},
		{&c.WebhookRetryBase, "SIM_WEBHOOK_RETRY_BASE", time.Second},
		{&c.EfiSettleDelay, "SIM_EFI_SETTLE_DELAY", 5 * time.Second},
		{&c.EfiRefundDelay, "SIM_EFI_REFUND_DELAY", time.Second},
	} {
		if *d.dst, err = duration(getenv, d.name, d.def); err != nil {
			return c, err
		}
	}
	return c, nil
}

func str(getenv func(string) string, name, def string) string {
	if v := strings.TrimSpace(getenv(name)); v != "" {
		return v
	}
	return def
}

func intVal(getenv func(string) string, name string, def int) (int, error) {
	v := strings.TrimSpace(getenv(name))
	if v == "" {
		return def, nil
	}
	n, err := strconv.Atoi(v)
	if err != nil || n <= 0 {
		return 0, fmt.Errorf("%s inválido: %q", name, v)
	}
	return n, nil
}

func duration(getenv func(string) string, name string, def time.Duration) (time.Duration, error) {
	v := strings.TrimSpace(getenv(name))
	if v == "" {
		return def, nil
	}
	d, err := time.ParseDuration(v)
	if err != nil || d <= 0 {
		return 0, fmt.Errorf("%s inválido: %q", name, v)
	}
	return d, nil
}
