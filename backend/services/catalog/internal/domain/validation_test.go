package domain

import (
	"fmt"
	"strings"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

func fieldsOf(t *testing.T, err error) []string {
	t.Helper()
	if err == nil {
		return nil
	}
	var ve *ValidationError
	require.ErrorAs(t, err, &ve)
	out := make([]string, len(ve.Fields))
	for i, f := range ve.Fields {
		out[i] = f.Field
	}
	return out
}

func validProduct() Product {
	img := "https://cdn.example.com/tv.jpg"
	return Product{SKU: "TV-55", Name: "Smart TV", PriceCents: 199990, Status: ProductActive, ImageURL: &img}
}

func TestProduct_Validate(t *testing.T) {
	tests := []struct {
		name   string
		mutate func(*Product)
		want   []string
	}{
		{"válido", func(*Product) {}, nil},
		{"sku minúsculo", func(p *Product) { p.SKU = "tv-55" }, []string{"sku"}},
		{"sku curto", func(p *Product) { p.SKU = "TV" }, []string{"sku"}},
		{"sku começando com hífen", func(p *Product) { p.SKU = "-TV55" }, []string{"sku"}},
		{"nome curto", func(p *Product) { p.Name = "TV" }, []string{"name"}},
		{"nome só de espaços", func(p *Product) { p.Name = "     " }, []string{"name"}},
		{"nome com acentos conta runas", func(p *Product) { p.Name = "Pão" }, nil},
		{"descrição longa", func(p *Product) { p.Description = strings.Repeat("a", 5001) }, []string{"description"}},
		{"preço zero", func(p *Product) { p.PriceCents = 0 }, []string{"price_cents"}},
		{"preço acima do teto", func(p *Product) { p.PriceCents = MaxPriceCents + 1 }, []string{"price_cents"}},
		{"status desconhecido", func(p *Product) { p.Status = "archived" }, []string{"status"}},
		{"imagem sem esquema", func(p *Product) { u := "cdn.example.com/x.jpg"; p.ImageURL = &u }, []string{"image_url"}},
		{"imagem ftp", func(p *Product) { u := "ftp://cdn.example.com/x.jpg"; p.ImageURL = &u }, []string{"image_url"}},
		{"vários erros de uma vez", func(p *Product) { p.SKU, p.PriceCents = "x", 0 }, []string{"sku", "price_cents"}},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			p := validProduct()
			tt.mutate(&p)
			assert.Equal(t, tt.want, fieldsOf(t, p.Validate()))
		})
	}
}

func TestProduct_Validate_Gallery(t *testing.T) {
	first := "https://cdn.example.com/a.jpg"
	photos := func(urls ...string) []GalleryImage {
		out := make([]GalleryImage, len(urls))
		for i, u := range urls {
			out[i].URL = u
		}
		return out
	}
	tooMany := make([]string, MaxGalleryImages+1)
	for i := range tooMany {
		tooMany[i] = fmt.Sprintf("https://cdn.example.com/%d.jpg", i)
	}
	for name, tc := range map[string]struct {
		image   *string
		gallery []GalleryImage
		valid   bool
	}{
		"fotos diferentes":  {&first, photos("https://cdn.example.com/b.jpg"), true},
		"sem galeria":       {nil, nil, true},
		"sem a primeira":    {nil, photos("https://cdn.example.com/b.jpg"), false},
		"repete a primeira": {&first, photos(first), false},
		"repete na galeria": {&first, photos("https://cdn.example.com/b.jpg", "https://cdn.example.com/b.jpg"), false},
		"não é http":        {&first, photos("javascript:alert(1)"), false},
		"fotos demais":      {&first, photos(tooMany...), false},
	} {
		t.Run(name, func(t *testing.T) {
			p := Product{SKU: "TV-55", Name: "Televisor", PriceCents: 100, Status: ProductActive, ImageURL: tc.image, Gallery: tc.gallery}
			err := p.Validate()
			if tc.valid {
				assert.NoError(t, err)
				return
			}
			assert.Contains(t, fieldsOf(t, err), "more_image_urls")
		})
	}
}

func TestProduct_Normalize(t *testing.T) {
	blank := "   "
	p := Product{SKU: " TV-55 ", Name: "  Smart TV ", Description: " x ", ImageURL: &blank}
	p.Normalize()

	assert.Equal(t, "TV-55", p.SKU)
	assert.Equal(t, "Smart TV", p.Name)
	assert.Equal(t, "x", p.Description)
	assert.Nil(t, p.ImageURL, "URL em branco vira ausente")

	img := " https://x.com/a.png "
	p.ImageURL = &img
	p.Normalize()
	assert.Equal(t, "https://x.com/a.png", *p.ImageURL)
}

func TestProduct_PriceChanged(t *testing.T) {
	before := Product{PriceCents: 100, Currency: "BRL"}
	assert.False(t, Product{PriceCents: 100, Currency: "BRL"}.PriceChanged(before))
	assert.True(t, Product{PriceCents: 90, Currency: "BRL"}.PriceChanged(before))
}

func TestCategory_Validate(t *testing.T) {
	id := uuid.New()
	tests := []struct {
		name string
		cat  Category
		want []string
	}{
		{"válida", Category{Slug: "smart-tvs", Name: "Smart TVs"}, nil},
		{"slug com maiúscula", Category{Slug: "Smart", Name: "Smart TVs"}, []string{"slug"}},
		{"slug com hífen duplo", Category{Slug: "smart--tv", Name: "Smart TVs"}, []string{"slug"}},
		{"slug longo", Category{Slug: strings.Repeat("a", 81), Name: "Smart TVs"}, []string{"slug"}},
		{"nome curto", Category{Slug: "tv", Name: "T"}, []string{"name"}},
		{"pai de si mesma", Category{ID: id, ParentID: &id, Slug: "tv", Name: "TVs"}, []string{"parent_id"}},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			assert.Equal(t, tt.want, fieldsOf(t, tt.cat.Validate()))
		})
	}
}

func TestCampaign_Validate(t *testing.T) {
	start := time.Date(2026, 11, 27, 0, 0, 0, 0, time.UTC)
	valid := Campaign{Slug: "black-friday", Name: "Black Friday", DiscountPercent: 30, StartsAt: start, EndsAt: start.Add(time.Hour)}
	tests := []struct {
		name   string
		mutate func(*Campaign)
		want   []string
	}{
		{"válida", func(*Campaign) {}, nil},
		{"desconto zero", func(c *Campaign) { c.DiscountPercent = 0 }, []string{"discount_percent"}},
		{"desconto acima de 90", func(c *Campaign) { c.DiscountPercent = 91 }, []string{"discount_percent"}},
		{"fim igual ao início", func(c *Campaign) { c.EndsAt = c.StartsAt }, []string{"ends_at"}},
		{"sem datas", func(c *Campaign) { c.StartsAt, c.EndsAt = time.Time{}, time.Time{} }, []string{"starts_at", "ends_at"}},
		{"slug inválido", func(c *Campaign) { c.Slug = "Black Friday" }, []string{"slug"}},
		{"nome curto", func(c *Campaign) { c.Name = "BF" }, []string{"name"}},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			c := valid
			tt.mutate(&c)
			assert.Equal(t, tt.want, fieldsOf(t, c.Validate()))
		})
	}
}

func TestValidationError(t *testing.T) {
	err := Invalid("category_id", "categoria não existe")

	var ve *ValidationError
	require.ErrorAs(t, err, &ve)
	assert.Equal(t, "dados inválidos: category_id: categoria não existe", err.Error())
}
