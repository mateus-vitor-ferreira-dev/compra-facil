package repository

import (
	"encoding/base64"
	"encoding/json"
	"fmt"
	"time"

	"github.com/google/uuid"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

const (
	defaultLimit = 20
	maxLimit     = 100
	// maxSearchOffset limita a profundidade da busca paginada por offset.
	maxSearchOffset = 1000
)

// cursor é serializado em JSON + base64url; o cliente o trata como opaco.
// Listagens ordenadas usam keyset: (T, ID) nos mais novos, (V, ID) nas ordens por preço e desconto.
// A busca por relevância usa offset (O). S é a ordem que gerou o cursor: um cursor só vale para ela.
type cursor struct {
	S  domain.ProductSort `json:"s,omitempty"`
	T  *time.Time         `json:"t,omitempty"`
	V  *int64             `json:"v,omitempty"`
	ID uuid.UUID          `json:"i,omitzero"`
	O  int                `json:"o,omitempty"`
}

func (c cursor) encode() string {
	b, _ := json.Marshal(c) //nolint:errchkjson // struct simples, Marshal não falha
	return base64.RawURLEncoding.EncodeToString(b)
}

func decodeCursor(s string) (cursor, error) {
	var c cursor
	if s == "" {
		return c, nil
	}
	b, err := base64.RawURLEncoding.DecodeString(s)
	if err != nil {
		return c, fmt.Errorf("%w: %w", domain.ErrInvalidCursor, err)
	}
	if err := json.Unmarshal(b, &c); err != nil {
		return c, fmt.Errorf("%w: %w", domain.ErrInvalidCursor, err)
	}
	if c.O < 0 || c.O > maxSearchOffset {
		return c, domain.ErrInvalidCursor
	}
	return c, nil
}

func clampLimit(limit int) int {
	switch {
	case limit <= 0:
		return defaultLimit
	case limit > maxLimit:
		return maxLimit
	default:
		return limit
	}
}
