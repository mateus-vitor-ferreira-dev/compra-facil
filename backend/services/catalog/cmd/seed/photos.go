package main

import (
	_ "embed"
	"encoding/json"
	"fmt"
	"strings"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

// photos.json é a cópia de infra/media/manifest.json (gerado por scripts/media/product_images.py):
// as variantes de cada foto. Atualize com `go generate ./cmd/seed`; TestPhotosManifestInSync falha
// se a cópia divergir.
//
//go:generate cp ../../../../../infra/media/manifest.json photos.json
//go:embed photos.json
var photosJSON []byte

// minPhotosPerSubcategory: com menos que isto, cards vizinhos de uma listagem repetiriam a foto (#178).
const minPhotosPerSubcategory = 10

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

// photoCount é quantas fotos a subcategoria tem no manifesto (<slug>-1 ... <slug>-N, sem buracos).
func photoCount(photos map[string]photo, subSlug string) int {
	n := 0
	for {
		if _, ok := photos[fmt.Sprintf("%s-%d", subSlug, n+1)]; !ok {
			return n
		}
		n++
	}
}

// photoFor devolve a foto n (a partir de 0, em ciclo) da subcategoria: a URL da maior variante e a
// ProductImage com todas.
func photoFor(photos map[string]photo, mediaURL, subSlug string, n int) (string, *domain.ProductImage) {
	count := photoCount(photos, subSlug)
	if count == 0 {
		panic("subcategoria " + subSlug + " sem fotos no manifesto: rode scripts/media/product_images.py build e go generate ./cmd/seed")
	}
	name := fmt.Sprintf("%s-%d", subSlug, n%count+1)
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
