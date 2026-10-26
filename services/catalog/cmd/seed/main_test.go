package main

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"math/rand/v2"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/events"
)

func subsForTest() []seededSub {
	var subs []seededSub
	for _, root := range catalogTree {
		for _, sub := range root.children {
			subs = append(subs, seededSub{subcategory: sub, id: uuid.New(), rootSlug: root.slug})
		}
	}
	return subs
}

func TestGenerateProducts_DeterministicAndValid(t *testing.T) {
	now := time.Date(2026, 11, 27, 0, 0, 0, 0, time.UTC)
	subs := subsForTest()
	gen := func() []seedProduct {
		return generateProducts(rand.New(rand.NewPCG(42, 7)), subs, 500, now, "http://localhost/media/")
	}

	a, b := gen(), gen()
	require.Len(t, a, 500)
	assert.Equal(t, a, b, "mesma semente, mesmos produtos")

	skus := map[string]bool{}
	for _, p := range a {
		assert.False(t, skus[p.sku], "SKU repetido: %s", p.sku)
		skus[p.sku] = true
		assert.Regexp(t, `^[A-Z0-9][A-Z0-9-]{2,31}$`, p.sku, "SKU no formato do domínio")
		assert.Equal(t, int64(90), p.price%100, "preço termina em ,90")
		assert.Contains(t, []string{"active", "inactive", "draft"}, p.status)
		assert.Equal(t, uuid.Version(4), p.id.Version())
		assert.Regexp(t, `^http://localhost/media/produtos/[a-z-]+-[1-4]\.[0-9a-f]{8}-[0-9]+\.webp$`, p.imageURL, "foto da subcategoria, sem barra dupla")
		require.NotNil(t, p.image)
		require.NotEmpty(t, p.image.Variants)
		assert.Equal(t, p.imageURL, p.image.Variants[len(p.image.Variants)-1].URL, "image_url é a maior variante")
		assert.Equal(t, p.image.Width, p.image.Variants[len(p.image.Variants)-1].Width)
		assert.Equal(t, p.imageURL, *p.domain().ImageURL, "o evento leva a mesma foto")
	}
}

// toda foto que o seed pode sortear está no manifesto, com as variantes em infra/media/produtos
func TestPhotos_ExistForEverySubcategory(t *testing.T) {
	dir := filepath.Join("..", "..", "..", "..", "infra", "media")
	if _, err := os.Stat(dir); os.IsNotExist(err) {
		t.Skip("infra/media fora do contexto (ex.: build da imagem)")
	}
	photos, err := loadPhotos()
	require.NoError(t, err)
	for _, root := range catalogTree {
		for _, sub := range root.children {
			for n := 1; n <= photosPerSubcategory; n++ {
				name := fmt.Sprintf("%s-%d", sub.slug, n)
				p, ok := photos[name]
				if !assert.True(t, ok, "falta %s no manifesto: rode scripts/media/product_images.py build", name) {
					continue
				}
				assert.Equal(t, p.Width, p.Height, "%s: quadrada", name)
				assert.GreaterOrEqual(t, p.Width, 600, "%s: pequena demais para a página do produto", name)
				for i, v := range p.Variants {
					assert.FileExists(t, filepath.Join(dir, "produtos", v.File))
					if i > 0 {
						assert.Greater(t, v.Width, p.Variants[i-1].Width, "%s: variantes em ordem crescente", name)
					}
				}
			}
		}
	}
}

func TestPhotosManifestInSync(t *testing.T) {
	source, err := os.ReadFile(filepath.Join("..", "..", "..", "..", "infra", "media", "manifest.json"))
	if os.IsNotExist(err) {
		t.Skip("infra/media fora do contexto (ex.: build da imagem)")
	}
	require.NoError(t, err)
	// cópia byte a byte, como a do contrato (não só JSON equivalente)
	assert.True(t, bytes.Equal(source, photosJSON), "photos.json desatualizado: rode go generate ./cmd/seed")
}

func TestRoundPrice(t *testing.T) {
	assert.Equal(t, int64(123490), roundPrice(123456))
	assert.Equal(t, int64(90), roundPrice(50), "nunca zero ou negativo")
}

func TestSeedEvents_OnePerProductAndCampaign(t *testing.T) {
	now := time.Date(2026, 11, 27, 0, 0, 0, 0, time.UTC)
	products := generateProducts(rand.New(rand.NewPCG(42, 7)), subsForTest(), 20, now, "http://localhost/media")
	campaign := seededCampaign{
		Campaign:   domain.Campaign{ID: uuid.New(), Slug: "bf", Name: "BF", DiscountPercent: 30, StartsAt: now, EndsAt: now.Add(time.Hour), Version: 1},
		productIDs: []uuid.UUID{products[0].id, products[1].id},
	}

	msgs, err := seedEvents(context.Background(), products, []seededCampaign{campaign}, now)
	require.NoError(t, err)
	require.Len(t, msgs, 21)

	correlation := ""
	for i, m := range msgs[:20] {
		assert.Equal(t, events.TypeProductCreated, m.Type)
		assert.Equal(t, products[i].id.String(), m.Key)
		var env struct {
			CorrelationID string `json:"correlation_id"`
			Data          struct {
				Version int    `json:"version"`
				Status  string `json:"status"`
			} `json:"data"`
		}
		require.NoError(t, json.Unmarshal(m.Payload, &env))
		assert.Equal(t, 1, env.Data.Version)
		assert.Equal(t, products[i].status, env.Data.Status)
		if correlation == "" {
			correlation = env.CorrelationID
		}
		assert.Equal(t, correlation, env.CorrelationID, "a carga inteira é um fluxo só")
	}
	last := msgs[20]
	assert.Equal(t, events.TypeCampaignCreated, last.Type, "campanhas depois dos produtos que referenciam")
	assert.Contains(t, string(last.Payload), products[0].id.String())
}
