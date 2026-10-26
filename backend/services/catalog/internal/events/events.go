// Package events monta os eventos que o catálogo publica no Kafka e os entrega a partir do outbox.
//
// Contrato: contracts/asyncapi/catalog.v1.yaml. O fluxo é:
//  1. o repositório grava a escrita e o evento (Message) na tabela outbox, na mesma transação;
//  2. o Relay lê o outbox em ordem, publica pelo Publisher e apaga o que foi publicado.
//
// A entrega é at-least-once: se o Kafka confirmar e o processo cair antes de apagar a linha, o
// evento sai de novo. O consumidor deduplica por event_id.
package events

import (
	"context"
	"encoding/json"
	"fmt"
	"time"

	"github.com/google/uuid"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/propagation"
)

// Producer é o valor de `producer` no envelope de todo evento do catálogo.
const Producer = "catalog"

// Message é um evento pronto para o outbox: o envelope já serializado e o destino no Kafka.
type Message struct {
	EventID uuid.UUID
	Topic   string
	Key     string
	Type    string
	Payload []byte
	// Headers do Kafka; hoje só o trace W3C (traceparent/tracestate), quando houver span ativo.
	Headers map[string]string
}

// envelope segue EventEnvelope em contracts/asyncapi/common/v1/common.yaml.
type envelope struct {
	EventID       uuid.UUID `json:"event_id"`
	EventType     string    `json:"event_type"`
	EventVersion  int       `json:"event_version"`
	OccurredAt    time.Time `json:"occurred_at"`
	Producer      string    `json:"producer"`
	CorrelationID string    `json:"correlation_id"`
	// sempre nil no catálogo: os eventos dele nascem de requisições HTTP, não de outros eventos
	CausationID *uuid.UUID `json:"causation_id"`
	Data        any        `json:"data"`
}

// newMessage monta o envelope (versão 1 do formato de data) e serializa.
func newMessage(ctx context.Context, topic, key, eventType string, at time.Time, data any) (Message, error) {
	id, err := uuid.NewV7()
	if err != nil {
		return Message{}, fmt.Errorf("gerando event_id: %w", err)
	}
	payload, err := json.Marshal(envelope{
		EventID:       id,
		EventType:     eventType,
		EventVersion:  1,
		OccurredAt:    at.UTC(),
		Producer:      Producer,
		CorrelationID: correlationID(ctx),
		Data:          data,
	})
	if err != nil {
		return Message{}, fmt.Errorf("serializando %s: %w", eventType, err)
	}

	headers := propagation.MapCarrier{}
	otel.GetTextMapPropagator().Inject(ctx, headers)

	return Message{EventID: id, Topic: topic, Key: key, Type: eventType, Payload: payload, Headers: headers}, nil
}

type correlationKey struct{}

// WithCorrelationID guarda no contexto o id do fluxo de negócio (o X-Request-ID da requisição).
func WithCorrelationID(ctx context.Context, id string) context.Context {
	return context.WithValue(ctx, correlationKey{}, id)
}

// correlationID devolve o id guardado por WithCorrelationID; sem ele (seed, testes), cria um novo:
// o evento começa um fluxo próprio.
func correlationID(ctx context.Context) string {
	if id, ok := ctx.Value(correlationKey{}).(string); ok && id != "" {
		return id
	}
	return uuid.NewString()
}
