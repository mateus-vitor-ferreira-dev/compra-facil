// Package migrations embute os arquivos SQL do golang-migrate no binário.
//
// Novo arquivo: migrate create -ext sql -dir migrations -seq <nome>
package migrations

import (
	"embed"
	"errors"
	"fmt"
	"strings"

	"github.com/golang-migrate/migrate/v4"
	_ "github.com/golang-migrate/migrate/v4/database/pgx/v5" // registra o esquema pgx5://
	"github.com/golang-migrate/migrate/v4/source/iofs"
)

//go:embed *.sql
var files embed.FS

// Up aplica as migrações pendentes. Usa uma conexão própria (o driver fecha a que recebe).
func Up(dsn string) error {
	return run(dsn, func(m *migrate.Migrate) error { return m.Up() })
}

// Down desfaz todas as migrações.
func Down(dsn string) error {
	return run(dsn, func(m *migrate.Migrate) error { return m.Down() })
}

func run(dsn string, step func(*migrate.Migrate) error) (err error) {
	src, err := iofs.New(files, ".")
	if err != nil {
		return fmt.Errorf("lendo migrações embutidas: %w", err)
	}
	m, err := migrate.NewWithSourceInstance("iofs", src, toPgx5URL(dsn))
	if err != nil {
		return fmt.Errorf("conectando para migrar: %w", err)
	}
	defer func() {
		srcErr, dbErr := m.Close()
		if err == nil {
			err = errors.Join(srcErr, dbErr)
		}
	}()

	if err := step(m); err != nil && !errors.Is(err, migrate.ErrNoChange) {
		return fmt.Errorf("migrando: %w", err)
	}
	return nil
}

// toPgx5URL troca postgres:// (usado pelo GORM) pelo esquema do driver pgx do golang-migrate.
func toPgx5URL(dsn string) string {
	for _, prefix := range []string{"postgres://", "postgresql://"} {
		if rest, ok := strings.CutPrefix(dsn, prefix); ok {
			return "pgx5://" + rest
		}
	}
	return dsn
}
