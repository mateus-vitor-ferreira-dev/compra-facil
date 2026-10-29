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

	PIXExpiration time.Duration
	ReviewDelay   time.Duration
	// PublicURL é de onde o navegador acessa o simulador (o ticket_url do PIX aponta para lá)
	PublicURL string

	WebhookURL       string
	WebhookSecret    string
	WebhookRetryBase time.Duration
}

// Load lê o ambiente aplicando os padrões; valor inválido é erro, não padrão silencioso.
func Load() (Config, error) { return load(os.Getenv) }

func load(getenv func(string) string) (Config, error) {
	c := Config{
		LogLevel:      str(getenv, "LOG_LEVEL", "info"),
		AccessToken:   str(getenv, "SIM_ACCESS_TOKEN", "TEST-sim-access-token"),
		PublicKey:     str(getenv, "SIM_PUBLIC_KEY", "TEST-sim-public-key"),
		PublicURL:     strings.TrimRight(str(getenv, "SIM_PUBLIC_URL", "http://localhost:8092"), "/"),
		WebhookURL:    strings.TrimSpace(getenv("SIM_WEBHOOK_URL")),
		WebhookSecret: str(getenv, "SIM_WEBHOOK_SECRET", "sim-secret"),
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
