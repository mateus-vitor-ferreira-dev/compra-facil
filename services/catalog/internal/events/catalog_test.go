package events

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/santhosh-tekuri/jsonschema/v6"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"go.yaml.in/yaml/v3"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
)

// Teste de contrato: o JSON de cada evento é validado contra o payload declarado no AsyncAPI
// (contracts/asyncapi/catalog.v1.yaml), seguindo os $ref até common e até os schemas da OpenAPI.
// Mudou o contrato ou o evento sem mudar o outro, este teste quebra.

const contractsDir = "../../../../contracts/asyncapi"

// yamlLoader deixa o compilador de JSON Schema ler os contratos em YAML.
type yamlLoader struct{}

func (yamlLoader) Load(url string) (any, error) {
	b, err := os.ReadFile(strings.TrimPrefix(url, "file://"))
	if err != nil {
		return nil, err //nolint:wrapcheck // erro de leitura já traz o caminho
	}
	var v any
	if err := yaml.Unmarshal(b, &v); err != nil {
		return nil, fmt.Errorf("%s: %w", url, err)
	}
	return v, nil
}

func contractSchema(t *testing.T, eventType string) *jsonschema.Schema {
	t.Helper()
	dir, err := filepath.Abs(contractsDir)
	require.NoError(t, err)
	c := jsonschema.NewCompiler()
	c.UseLoader(yamlLoader{})
	c.AssertFormat() // uuid, date-time, email passam a ser checados
	sch, err := c.Compile("file://" + dir + "/catalog.v1.yaml#/components/messages/" + eventType + "/payload")
	require.NoError(t, err)
	return sch
}

func assertMatchesContract(t *testing.T, m Message) {
	t.Helper()
	inst, err := jsonschema.UnmarshalJSON(bytes.NewReader(m.Payload))
	require.NoError(t, err)
	assert.NoError(t, contractSchema(t, m.Type).Validate(inst), "payload: %s", m.Payload)
}

var (
	at      = time.Date(2026, 11, 28, 3, 0, 0, 0, time.FixedZone("BRT", -3*60*60))
	product = domain.Product{
		ID: uuid.MustParse("0f8fad5b-d9cb-469f-a165-70867728950e"), SKU: "ELE-000123", Name: "Fone Sonix Pro",
		CategoryID: uuid.MustParse("6a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d"), PriceCents: 19990, Currency: "BRL",
		Status: domain.ProductActive, Version: 3, UpdatedAt: at,
	}
	campaign = domain.Campaign{
		ID: uuid.MustParse("1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e5f"), Slug: "black-friday", Name: "Black Friday",
		DiscountPercent: 30, StartsAt: at, EndsAt: at.Add(72 * time.Hour), Version: 2,
	}
)

func TestEvents_MatchContract(t *testing.T) {
	ctx := context.Background()
	build := map[string]func() (Message, error){
		TypeProductCreated: func() (Message, error) { return ProductCreated(ctx, product, at) },
		TypeProductUpdated: func() (Message, error) { return ProductUpdated(ctx, product, at) },
		TypeProductPriceChanged: func() (Message, error) {
			return ProductPriceChanged(ctx, product, 24990, "BRL", at)
		},
		TypeProductDeleted:  func() (Message, error) { return ProductDeleted(ctx, product.ID, product.SKU, 4, at) },
		TypeCampaignCreated: func() (Message, error) { return CampaignCreated(ctx, campaign, nil, at) },
		TypeCampaignUpdated: func() (Message, error) {
			return CampaignUpdated(ctx, campaign, []uuid.UUID{product.ID, uuid.New()}, at)
		},
		TypeCampaignDeleted: func() (Message, error) { return CampaignDeleted(ctx, campaign.ID, 3, at) },
	}
	for eventType, fn := range build {
		t.Run(eventType, func(t *testing.T) {
			m, err := fn()
			require.NoError(t, err)
			assert.Equal(t, eventType, m.Type)
			assertMatchesContract(t, m)
		})
	}
}

// Garante que o teste de contrato não passa por engano (schema vazio, $ref não resolvido).
func TestContract_RejectsWrongPayload(t *testing.T) {
	m, err := ProductUpdated(context.Background(), product, at)
	require.NoError(t, err)

	var v map[string]any
	require.NoError(t, json.Unmarshal(m.Payload, &v))
	v["data"].(map[string]any)["price"] = map[string]any{"amount_cents": 100, "currency": "real"}
	delete(v["data"].(map[string]any), "sku")
	bad, err := json.Marshal(v)
	require.NoError(t, err)

	inst, err := jsonschema.UnmarshalJSON(bytes.NewReader(bad))
	require.NoError(t, err)
	err = contractSchema(t, TypeProductUpdated).Validate(inst)
	require.Error(t, err)
	assert.Contains(t, err.Error(), "sku")
	assert.Contains(t, err.Error(), "currency")
}

func TestMessage_KeyTopicAndEnvelope(t *testing.T) {
	ctx := WithCorrelationID(context.Background(), "req-123")
	m, err := ProductPriceChanged(ctx, product, 24990, "BRL", at)
	require.NoError(t, err)

	assert.Equal(t, TopicProducts, m.Topic)
	assert.Equal(t, product.ID.String(), m.Key, "chave = product_id: ordem por produto")

	var env struct {
		EventID       uuid.UUID `json:"event_id"`
		CorrelationID string    `json:"correlation_id"`
		OccurredAt    string    `json:"occurred_at"`
		Data          struct {
			OldPrice struct {
				AmountCents int64 `json:"amount_cents"`
			} `json:"old_price"`
		} `json:"data"`
	}
	require.NoError(t, json.Unmarshal(m.Payload, &env))
	assert.Equal(t, m.EventID, env.EventID)
	assert.Equal(t, "req-123", env.CorrelationID)
	assert.Equal(t, "2026-11-28T06:00:00Z", env.OccurredAt, "sempre em UTC")
	assert.EqualValues(t, 24990, env.Data.OldPrice.AmountCents)
}

func TestMessage_WithoutCorrelationStartsNewFlow(t *testing.T) {
	a, err := ProductCreated(context.Background(), product, at)
	require.NoError(t, err)
	b, err := ProductCreated(context.Background(), product, at)
	require.NoError(t, err)

	assert.NotEqual(t, a.EventID, b.EventID)
	assert.NotEqual(t, correlationOf(t, a), correlationOf(t, b))
}

func TestCampaignSnapshot_SortsAndNeverNull(t *testing.T) {
	a, b := uuid.MustParse("ffffffff-0000-4000-8000-000000000000"), uuid.MustParse("00000000-0000-4000-8000-000000000001")
	m, err := CampaignUpdated(context.Background(), campaign, []uuid.UUID{a, b}, at)
	require.NoError(t, err)
	assert.Contains(t, string(m.Payload), `"product_ids":["`+b.String()+`","`+a.String()+`"]`)

	m, err = CampaignCreated(context.Background(), campaign, nil, at)
	require.NoError(t, err)
	assert.Contains(t, string(m.Payload), `"product_ids":[]`)
}

func correlationOf(t *testing.T, m Message) string {
	t.Helper()
	var env struct {
		CorrelationID string `json:"correlation_id"`
	}
	require.NoError(t, json.Unmarshal(m.Payload, &env))
	return env.CorrelationID
}
