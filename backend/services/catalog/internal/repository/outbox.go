package repository

import (
	"context"
	"encoding/json"
	"fmt"
	"time"

	"github.com/google/uuid"
	"gorm.io/gorm"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/events"
)

// relayLockKey é a chave do advisory lock do relay: só uma réplica do catálogo publica por vez,
// senão duas poderiam publicar eventos do mesmo produto fora de ordem. Valor arbitrário e fixo.
const relayLockKey int64 = 0x63617461_6c6f6721 // "catalog!"

type outboxRecord struct {
	ID         int64 `gorm:"primaryKey;->"` // identity: o banco gera
	EventID    uuid.UUID
	Topic      string
	MessageKey string
	EventType  string
	Payload    json.RawMessage `gorm:"type:json"`
	Headers    json.RawMessage `gorm:"type:json"`
	CreatedAt  time.Time       `gorm:"->"`
}

func (outboxRecord) TableName() string { return "outbox" }

// enqueue grava os eventos na transação da escrita: ou os dois entram, ou nenhum.
func enqueue(tx *gorm.DB, msgs ...events.Message) error {
	if len(msgs) == 0 {
		return nil
	}
	recs := make([]outboxRecord, len(msgs))
	for i, m := range msgs {
		headers, err := json.Marshal(m.Headers)
		if err != nil {
			return fmt.Errorf("headers de %s: %w", m.Type, err)
		}
		recs[i] = outboxRecord{
			EventID: m.EventID, Topic: m.Topic, MessageKey: m.Key, EventType: m.Type,
			Payload: m.Payload, Headers: headers,
		}
	}
	if err := tx.Omit("ID", "CreatedAt").Create(&recs).Error; err != nil {
		return fmt.Errorf("gravando outbox: %w", err)
	}
	return nil
}

// buildAll monta vários eventos e para no primeiro erro.
func buildAll(builders ...func() (events.Message, error)) ([]events.Message, error) {
	msgs := make([]events.Message, 0, len(builders))
	for _, build := range builders {
		m, err := build()
		if err != nil {
			return nil, err //nolint:wrapcheck // o builder já diz qual evento falhou
		}
		msgs = append(msgs, m)
	}
	return msgs, nil
}

// OutboxRepository é o lado de leitura do outbox, usado pelo relay.
type OutboxRepository struct{ db *gorm.DB }

func NewOutboxRepository(db *gorm.DB) *OutboxRepository { return &OutboxRepository{db: db} }

var _ events.Outbox = (*OutboxRepository)(nil)

// Drain entrega a publish os eventos mais antigos (até limit), em ordem, e apaga os que foram
// publicados. Se publish falhar, nada é apagado e os mesmos eventos voltam na próxima chamada.
// Outra réplica com o lock em mãos faz Drain devolver 0 sem erro.
func (r *OutboxRepository) Drain(ctx context.Context, limit int, publish func(context.Context, []events.Message) error) (int, error) {
	n := 0
	err := r.db.WithContext(ctx).Transaction(func(tx *gorm.DB) error {
		var locked bool
		if err := tx.Raw("SELECT pg_try_advisory_xact_lock(?)", relayLockKey).Scan(&locked).Error; err != nil {
			return err
		}
		if !locked {
			return nil
		}

		var recs []outboxRecord
		if err := tx.Order("id").Limit(limit).Find(&recs).Error; err != nil {
			return err
		}
		if len(recs) == 0 {
			return nil
		}

		msgs := make([]events.Message, len(recs))
		ids := make([]int64, len(recs))
		for i, rec := range recs {
			var headers map[string]string
			if err := json.Unmarshal(rec.Headers, &headers); err != nil {
				return fmt.Errorf("headers do evento %s: %w", rec.EventID, err)
			}
			msgs[i] = events.Message{
				EventID: rec.EventID, Topic: rec.Topic, Key: rec.MessageKey, Type: rec.EventType,
				Payload: rec.Payload, Headers: headers,
			}
			ids[i] = rec.ID
		}

		if err := publish(ctx, msgs); err != nil {
			return err
		}
		if err := tx.Delete(&outboxRecord{}, ids).Error; err != nil {
			return err
		}
		n = len(recs)
		return nil
	})
	if err != nil {
		return 0, fmt.Errorf("drenando o outbox: %w", err)
	}
	return n, nil
}
