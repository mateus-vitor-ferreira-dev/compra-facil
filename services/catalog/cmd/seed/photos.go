package main

import (
	_ "embed"
	"encoding/json"
	"fmt"
	"math/rand/v2"
	"strings"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/domain"
)

// photos.json é a cópia de infra/media/manifest.json (gerado por scripts/media/product_images.py):
// as variantes de cada foto. Atualize com `go generate ./cmd/seed`; TestPhotosManifestInSync falha
// se a cópia divergir.
//
//go:generate cp ../../../../infra/media/manifest.json photos.json
//go:embed photos.json
var photosJSON []byte

// photosPerSubcategory é quantas fotos cada subcategoria tem (<slug>-1 ... <slug>-N).
const photosPerSubcategory = 4

type photo struct {
	Width    int `json:"width"`
	Height   int `json:"height"`
	Variants []struct {
		Width int    `json:"width"`
		File  string `json:"file"`
	} `json:"variants"`
}

func loadPhotos() (map[string]photo, error) {
	var photos map[string]photo
	if err := json.Unmarshal(photosJSON, &photos); err != nil {
		return nil, fmt.Errorf("photos.json: %w", err)
	}
	return photos, nil
}

// photoFor sorteia uma das fotos da subcategoria: a URL da maior variante e a ProductImage com todas.
func photoFor(rng *rand.Rand, photos map[string]photo, mediaURL, subSlug string) (string, *domain.ProductImage) {
	name := fmt.Sprintf("%s-%d", subSlug, rng.IntN(photosPerSubcategory)+1)
	p, ok := photos[name]
	if !ok || len(p.Variants) == 0 {
		panic("foto " + name + " fora do manifesto: rode scripts/media/product_images.py build e go generate ./cmd/seed")
	}
	base := strings.TrimRight(mediaURL, "/") + "/produtos/"
	img := &domain.ProductImage{Width: p.Width, Height: p.Height, Variants: make([]domain.ImageVariant, len(p.Variants))}
	for i, v := range p.Variants {
		img.Variants[i] = domain.ImageVariant{Width: v.Width, URL: base + v.File}
	}
	return img.Variants[len(img.Variants)-1].URL, img
}
