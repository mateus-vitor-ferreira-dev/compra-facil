// Package repository implementa os ports de domain com GORM sobre PostgreSQL.
package repository

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"time"

	"github.com/jackc/pgx/v5/pgconn"
	"gorm.io/driver/postgres"
	"gorm.io/gorm"
	"gorm.io/gorm/clause"
	"gorm.io/gorm/logger"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/config"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
)

var lockForUpdate = clause.Locking{Strength: clause.LockingStrengthUpdate}

// Open conecta ao Postgres com o pool configurado e logs de query lenta via slog.
func Open(cfg config.DBConfig, log *slog.Logger) (*gorm.DB, error) {
	db, err := gorm.Open(postgres.Open(cfg.DSN), &gorm.Config{
		Logger: logger.NewSlogLogger(log, logger.Config{
			SlowThreshold:             cfg.SlowQuery,
			LogLevel:                  logger.Warn,
			IgnoreRecordNotFoundError: true,
			ParameterizedQueries:      true, // não vaza valores nos logs
		}),
		TranslateError: true,
		NowFunc:        func() time.Time { return time.Now().UTC() },
	})
	if err != nil {
		return nil, fmt.Errorf("abrindo conexão: %w", err)
	}

	sqlDB, err := db.DB()
	if err != nil {
		return nil, fmt.Errorf("obtendo *sql.DB: %w", err)
	}
	sqlDB.SetMaxOpenConns(cfg.MaxOpenConns)
	sqlDB.SetMaxIdleConns(cfg.MaxIdleConns)
	sqlDB.SetConnMaxLifetime(cfg.ConnMaxLifetime)

	return db, nil
}

// Ping serve de check de readiness.
func Ping(db *gorm.DB) func(ctx context.Context) error {
	return func(ctx context.Context) error {
		sqlDB, err := db.DB()
		if err != nil {
			return fmt.Errorf("obtendo *sql.DB: %w", err)
		}
		if err := sqlDB.PingContext(ctx); err != nil {
			return fmt.Errorf("ping: %w", err)
		}
		return nil
	}
}

// mapError traduz erros do GORM/Postgres para os erros de domínio.
func mapError(err error) error {
	switch {
	case err == nil:
		return nil
	case errors.Is(err, domain.ErrNotFound), errors.Is(err, domain.ErrVersionConflict):
		return err // já é erro de domínio (devolvido de dentro de uma transação)
	case errors.Is(err, gorm.ErrRecordNotFound):
		return domain.ErrNotFound
	case errors.Is(err, gorm.ErrDuplicatedKey):
		return fmt.Errorf("%w: %w", domain.ErrConflict, err)
	case errors.Is(err, gorm.ErrForeignKeyViolated):
		return fmt.Errorf("%w: %w", domain.ErrInvalidReference, err)
	}
	// TranslateError não cobre CHECK; os services validam antes, isto é a última barreira.
	var pgErr *pgconn.PgError
	if errors.As(err, &pgErr) && pgErr.Code == "23514" {
		return fmt.Errorf("%w: %s", domain.ErrInvalidReference, pgErr.ConstraintName)
	}
	return fmt.Errorf("banco de dados: %w", err)
}
