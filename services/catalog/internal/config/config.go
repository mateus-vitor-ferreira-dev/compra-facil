// Package config carrega a configuração do serviço com Viper.
//
// Ordem de precedência (maior vence): variáveis de ambiente CATALOG_* > config.yaml > defaults.
// Ex.: http.port vira CATALOG_HTTP_PORT.
package config

import (
	"errors"
	"fmt"
	"strings"
	"time"

	"github.com/spf13/viper"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/auth"
)

const envPrefix = "CATALOG"

type Config struct {
	App  AppConfig  `mapstructure:"app"`
	HTTP HTTPConfig `mapstructure:"http"`
	GRPC GRPCConfig `mapstructure:"grpc"`
	Log  LogConfig  `mapstructure:"log"`
	DB   DBConfig   `mapstructure:"db"`
	Auth AuthConfig `mapstructure:"auth"`
}

type AppConfig struct {
	Name string `mapstructure:"name"`
	Env  string `mapstructure:"env"`
}

type HTTPConfig struct {
	Port            int           `mapstructure:"port"`
	ReadTimeout     time.Duration `mapstructure:"read_timeout"`
	WriteTimeout    time.Duration `mapstructure:"write_timeout"`
	ShutdownTimeout time.Duration `mapstructure:"shutdown_timeout"`
}

type GRPCConfig struct {
	Port       int  `mapstructure:"port"`
	Reflection bool `mapstructure:"reflection"`
}

func (g GRPCConfig) Addr() string { return fmt.Sprintf(":%d", g.Port) }

type DBConfig struct {
	DSN             string        `mapstructure:"dsn"`
	MaxOpenConns    int           `mapstructure:"max_open_conns"`
	MaxIdleConns    int           `mapstructure:"max_idle_conns"`
	ConnMaxLifetime time.Duration `mapstructure:"conn_max_lifetime"`
	SlowQuery       time.Duration `mapstructure:"slow_query"`
	MigrateOnStart  bool          `mapstructure:"migrate_on_start"`
}

// AuthConfig descreve os JWT aceitos nas rotas /v1/admin/*.
type AuthConfig struct {
	JWTSecret string        `mapstructure:"jwt_secret"`
	Issuer    string        `mapstructure:"issuer"`
	Audience  string        `mapstructure:"audience"`
	Leeway    time.Duration `mapstructure:"leeway"`
}

func (a AuthConfig) Options() auth.Options {
	return auth.Options{Secret: a.JWTSecret, Issuer: a.Issuer, Audience: a.Audience, Leeway: a.Leeway}
}

// DevJWTSecret é o segredo do ambiente local; fora dele o serviço não sobe com este valor.
const DevJWTSecret = "dev-only-compra-facil-jwt-secret-troque-em-producao" //nolint:gosec // segredo público de dev, barrado fora do local

type LogConfig struct {
	Level string `mapstructure:"level"`
}

func (h HTTPConfig) Addr() string { return fmt.Sprintf(":%d", h.Port) }

// Load lê o config.yaml (opcional) dos diretórios informados e aplica as variáveis de ambiente.
func Load(paths ...string) (Config, error) {
	v := viper.New()
	setDefaults(v)

	v.SetConfigName("config")
	v.SetConfigType("yaml")
	for _, p := range paths {
		v.AddConfigPath(p)
	}

	v.SetEnvPrefix(envPrefix)
	v.SetEnvKeyReplacer(strings.NewReplacer(".", "_"))
	v.AutomaticEnv()

	if err := v.ReadInConfig(); err != nil {
		var notFound viper.ConfigFileNotFoundError
		if !errors.As(err, &notFound) {
			return Config{}, fmt.Errorf("lendo config: %w", err)
		}
	}

	var cfg Config
	if err := v.Unmarshal(&cfg); err != nil {
		return Config{}, fmt.Errorf("decodificando config: %w", err)
	}

	return cfg, cfg.validate()
}

// setDefaults registra todas as chaves; o AutomaticEnv só enxerga chaves conhecidas.
func setDefaults(v *viper.Viper) {
	v.SetDefault("app.name", "catalog")
	v.SetDefault("app.env", "local")
	v.SetDefault("http.port", 8001)
	v.SetDefault("http.read_timeout", "5s")
	v.SetDefault("http.write_timeout", "10s")
	v.SetDefault("http.shutdown_timeout", "15s")
	v.SetDefault("grpc.port", 9001)
	v.SetDefault("grpc.reflection", true)
	v.SetDefault("log.level", "info")
	v.SetDefault("db.dsn", "postgres://catalog:compra-facil@localhost:55432/catalog?sslmode=disable")
	v.SetDefault("db.max_open_conns", 20)
	v.SetDefault("db.max_idle_conns", 10)
	v.SetDefault("db.conn_max_lifetime", "30m")
	v.SetDefault("db.slow_query", "200ms")
	v.SetDefault("db.migrate_on_start", true)
	v.SetDefault("auth.jwt_secret", DevJWTSecret)
	v.SetDefault("auth.issuer", "compra-facil")
	v.SetDefault("auth.audience", "catalog")
	v.SetDefault("auth.leeway", "30s")
}

func (c Config) validate() error {
	if c.HTTP.Port <= 0 || c.HTTP.Port > 65535 {
		return fmt.Errorf("http.port inválida: %d", c.HTTP.Port)
	}
	if c.GRPC.Port <= 0 || c.GRPC.Port > 65535 || c.GRPC.Port == c.HTTP.Port {
		return fmt.Errorf("grpc.port inválida: %d", c.GRPC.Port)
	}
	if c.DB.DSN == "" {
		return errors.New("db.dsn é obrigatório")
	}
	if len(c.Auth.JWTSecret) < auth.MinSecretLen {
		return errors.New("auth.jwt_secret deve ter pelo menos 32 caracteres")
	}
	if c.Auth.JWTSecret == DevJWTSecret && c.App.Env != "local" && c.App.Env != "compose" {
		return fmt.Errorf("auth.jwt_secret de desenvolvimento não pode ser usado em app.env=%s", c.App.Env)
	}
	switch strings.ToLower(c.Log.Level) {
	case "debug", "info", "warn", "error":
	default:
		return fmt.Errorf("log.level inválido: %q", c.Log.Level)
	}
	return nil
}
