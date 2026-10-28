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

	Email    string
	Password string
	TokenTTL time.Duration

	// OutOfStock lista vids que respondem estoque 0 (para forçar a recusa por falta de estoque).
	OutOfStock map[string]bool

	AcceptDelay  time.Duration
	ShipDelay    time.Duration
	DeliverDelay time.Duration

	WebhookURL       string
	WebhookSecret    string
	WebhookRetryBase time.Duration
	WebhookAttempts  int
	WebhookDuplicate bool
}

// Load lê o ambiente aplicando os padrões; valor inválido é erro, não padrão silencioso.
func Load() (Config, error) {
	return load(os.Getenv)
}

func load(getenv func(string) string) (Config, error) {
	c := Config{
		LogLevel:        str(getenv, "LOG_LEVEL", "info"),
		Email:           str(getenv, "SIM_EMAIL", "sim@comprafacil.local"),
		Password:        str(getenv, "SIM_PASSWORD", "sim"),
		TokenTTL:        15 * 24 * time.Hour,
		OutOfStock:      list(getenv("SIM_OUT_OF_STOCK_VIDS")),
		WebhookURL:      strings.TrimSpace(getenv("SIM_WEBHOOK_URL")),
		WebhookSecret:   str(getenv, "SIM_WEBHOOK_SECRET", "sim-secret"),
		WebhookAttempts: 5,
	}
	var err error
	if c.Port, err = intVal(getenv, "PORT", 8090); err != nil {
		return c, err
	}
	durations := []struct {
		dst  *time.Duration
		name string
		def  time.Duration
	}{
		{&c.AcceptDelay, "SIM_ACCEPT_DELAY", 2 * time.Second},
		{&c.ShipDelay, "SIM_SHIP_DELAY", 5 * time.Second},
		{&c.DeliverDelay, "SIM_DELIVER_DELAY", 10 * time.Second},
		{&c.WebhookRetryBase, "SIM_WEBHOOK_RETRY_BASE", time.Second},
	}
	for _, d := range durations {
		if *d.dst, err = duration(getenv, d.name, d.def); err != nil {
			return c, err
		}
	}
	if c.WebhookDuplicate, err = boolVal(getenv, "SIM_WEBHOOK_DUPLICATE"); err != nil {
		return c, err
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
	if err != nil || n <= 0 || n > 65535 {
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
	if err != nil || d < 0 {
		return 0, fmt.Errorf("%s inválido (use duração Go, ex.: 2s, 500ms): %q", name, v)
	}
	return d, nil
}

func boolVal(getenv func(string) string, name string) (bool, error) {
	v := strings.TrimSpace(getenv(name))
	if v == "" {
		return false, nil
	}
	b, err := strconv.ParseBool(v)
	if err != nil {
		return false, fmt.Errorf("%s inválido: %q", name, v)
	}
	return b, nil
}

func list(v string) map[string]bool {
	out := map[string]bool{}
	for item := range strings.SplitSeq(v, ",") {
		if item = strings.TrimSpace(item); item != "" {
			out[item] = true
		}
	}
	return out
}
