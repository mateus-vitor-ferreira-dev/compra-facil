package repository

import (
	"context"
	"flag"
	"fmt"
	"log/slog"
	"os"
	"testing"
	"time"

	"github.com/testcontainers/testcontainers-go"
	tcpostgres "github.com/testcontainers/testcontainers-go/modules/postgres"
	"gorm.io/gorm"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/config"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/migrations"
)

// Um Postgres real (mesma imagem do compose) para o pacote inteiro; cada teste limpa as tabelas.
var testDB *gorm.DB

func TestMain(m *testing.M) {
	flag.Parse()
	if testing.Short() {
		fmt.Println("repository: testes de integração pulados (-short)")
		os.Exit(0)
	}
	os.Exit(runWithPostgres(m))
}

func runWithPostgres(m *testing.M) int {
	ctx := context.Background()
	ctr, err := tcpostgres.Run(ctx, "postgres:17.11-alpine",
		tcpostgres.WithDatabase("catalog"),
		tcpostgres.WithUsername("catalog"),
		tcpostgres.WithPassword("catalog"),
		tcpostgres.BasicWaitStrategies(),
	)
	defer func() { _ = testcontainers.TerminateContainer(ctr) }()
	if err != nil {
		fmt.Println("subindo postgres:", err)
		return 1
	}

	dsn, err := ctr.ConnectionString(ctx, "sslmode=disable")
	if err != nil {
		fmt.Println("dsn:", err)
		return 1
	}
	// sobe e desce uma vez para provar que o down.sql também funciona
	for _, step := range []func(string) error{migrations.Up, migrations.Down, migrations.Up} {
		if err := step(dsn); err != nil {
			fmt.Println("migrações:", err)
			return 1
		}
	}

	testDB, err = Open(config.DBConfig{
		DSN: dsn, MaxOpenConns: 10, MaxIdleConns: 5, ConnMaxLifetime: time.Minute, SlowQuery: time.Second,
	}, slog.New(slog.NewTextHandler(os.Stderr, &slog.HandlerOptions{Level: slog.LevelWarn})))
	if err != nil {
		fmt.Println("conectando:", err)
		return 1
	}
	return m.Run()
}

func resetDB(t *testing.T) {
	t.Helper()
	err := testDB.Exec("TRUNCATE campaign_products, campaigns, products, categories CASCADE").Error
	if err != nil {
		t.Fatalf("truncate: %v", err)
	}
}
