package messaging

import (
	"encoding/json"
	"fmt"
	"time"
)

// Time é uma data RFC 3339 de um evento de outro serviço. O time.Time do Go lê a string do JSON sem
// desfazer os escapes, e o System.Text.Json do .NET escapa o '+' do fuso ("...+00:00"): este tipo
// decodifica a string antes de ler a data.
type Time struct{ time.Time }

func (t *Time) UnmarshalJSON(data []byte) error {
	var s string
	if err := json.Unmarshal(data, &s); err != nil {
		return fmt.Errorf("data: %w", err)
	}
	parsed, err := time.Parse(time.RFC3339Nano, s)
	if err != nil {
		return fmt.Errorf("data %q: %w", s, err)
	}
	t.Time = parsed
	return nil
}
