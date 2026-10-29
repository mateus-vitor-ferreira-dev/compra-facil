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

	ClientID     string
	ClientSecret string
	TokenTTL     time.Duration

	// CFOPs aceitos (venda à ordem: 5120 dentro do estado, 6120 fora)
	AllowedCFOP  []string
	CancelWindow time.Duration
}

// Load lê o ambiente aplicando os padrões; valor inválido é erro, não padrão silencioso.
func Load() (Config, error) { return load(os.Getenv) }

func load(getenv func(string) string) (Config, error) {
	c := Config{
		LogLevel:     str(getenv, "LOG_LEVEL", "info"),
		ClientID:     str(getenv, "SIM_CLIENT_ID", "sim-client"),
		ClientSecret: str(getenv, "SIM_CLIENT_SECRET", "sim-secret"),
	}
	for _, cfop := range strings.Split(str(getenv, "SIM_ALLOWED_CFOP", "5120,6120"), ",") {
		if cfop = strings.TrimSpace(cfop); cfop != "" {
			c.AllowedCFOP = append(c.AllowedCFOP, cfop)
		}
	}
	var err error
	if c.Port, err = intVal(getenv, "PORT", 8090); err != nil {
		return c, err
	}
	if c.TokenTTL, err = duration(getenv, "SIM_TOKEN_TTL", time.Hour); err != nil {
		return c, err
	}
	if c.CancelWindow, err = duration(getenv, "SIM_CANCEL_WINDOW", 24*time.Hour); err != nil {
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
