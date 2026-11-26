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

func TestProduct_Validate_Sizes(t *testing.T) {
	tests := []struct {
		name  string
		sizes []string
		ok    bool
	}{
		{"sem tamanho", nil, true},
		{"lista vazia", []string{}, true},
		{"P M G GG", []string{"P", "M", "G", "GG"}, true},
		{"numeração", []string{"36", "38", "40"}, true},
		{"repetido sem diferenciar maiúsculas", []string{"G", "g"}, false},
		{"tamanho longo", []string{strings.Repeat("X", MaxSizeLength+1)}, false},
		{"acima do máximo", strings.Split(strings.Repeat("a,", MaxSizes)+"z", ","), false},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			p := validProduct()
			p.Sizes = tt.sizes
			if tt.ok {
				assert.NoError(t, p.Validate())
			} else {
				assert.Equal(t, []string{"sizes"}, fieldsOf(t, p.Validate()))
			}
		})
	}
}

func TestProduct_Normalize_Sizes(t *testing.T) {
	p := Product{Sizes: []string{" P ", "", "  ", "M"}}
	p.Normalize()
	assert.Equal(t, []string{"P", "M"}, p.Sizes, "tira espaços e os vazios")

	p = Product{}
	p.Normalize()
	assert.Nil(t, p.Sizes, "nil continua nil (o PUT mantém os gravados)")
}

func TestProduct_HasSize(t *testing.T) {
	sem := Product{}
	assert.True(t, sem.HasSize(""))
	assert.False(t, sem.HasSize("M"), "produto sem tamanho não aceita um")

	com := Product{Sizes: []string{"P", "G"}}
	assert.True(t, com.HasSize("g"), "sem diferenciar maiúsculas")
	assert.False(t, com.HasSize("M"))
	assert.False(t, com.HasSize(""), "produto com tamanho exige a escolha")
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

func TestProduct_Validate_Colors(t *testing.T) {
	tests := []struct {
		name   string
		colors []string
		ok     bool
	}{
		{"sem cor", nil, true},
		{"Branco e Preto", []string{"Branco", "Preto", "Azul Marinho"}, true},
		{"repetida sem diferenciar maiúsculas", []string{"Preto", "preto"}, false},
		{"cor longa", []string{strings.Repeat("x", MaxColorLength+1)}, false},
		{"acima do máximo", strings.Split(strings.Repeat("c,", MaxColors)+"z", ","), false},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			p := validProduct()
			p.Colors = tt.colors
			if tt.ok {
				assert.NoError(t, p.Validate())
			} else {
				assert.Equal(t, []string{"colors"}, fieldsOf(t, p.Validate()))
			}
		})
	}
}

func TestProduct_ColorsNormalizeAndHasColor(t *testing.T) {
	p := Product{Colors: []string{" Branco ", "", "Preto"}}
	p.Normalize()
	assert.Equal(t, []string{"Branco", "Preto"}, p.Colors)
	assert.True(t, p.HasColor("preto"))
	assert.False(t, p.HasColor("Vinho"))
	assert.False(t, p.HasColor(""), "produto com cor exige a escolha")

	sem := Product{}
	sem.Normalize()
	assert.Nil(t, sem.Colors, "nil continua nil (o PUT mantém as gravadas)")
	assert.True(t, sem.HasColor(""))
	assert.False(t, sem.HasColor("Preto"))
}

func TestProduct_CheckSupplierProducts(t *testing.T) {
	feed := &SupplierInfo{Name: "supplier-sim", VID: "v1", CostCents: 100, CostCurrency: "BRL"}
	tests := []struct {
		name   string
		p      Product
		fields []string
	}{
		{"ativo sem cor com um código", Product{Status: ProductActive, SupplierProducts: map[string]string{NoColor: "6168525"}}, nil},
		{"ativo com o código de cada cor", Product{Status: ProductActive, Colors: []string{"Branco", "Preto"},
			SupplierProducts: map[string]string{"Branco": "6167609", "Preto": "6167609"}}, nil},
		{"vermelho em outro produto da Montink", Product{Status: ProductActive, Colors: []string{"Branco", "Vermelho"},
			SupplierProducts: map[string]string{"Branco": "6167609", "Vermelho": "6170001"}}, nil},
		{"ativo sem fornecedor", Product{Status: ProductActive}, []string{"supplier_products"}},
		{"ativo com mapa vazio", Product{Status: ProductActive, SupplierProducts: map[string]string{}}, []string{"supplier_products"}},
		{"ativo do feed de dropshipping", Product{Status: ProductActive, Supplier: feed}, nil},
		{"rascunho sem fornecedor", Product{Status: ProductDraft, Colors: []string{"Branco"}}, nil},
		{"inativo sem fornecedor", Product{Status: ProductInactive}, nil},
		{"cor à venda sem código", Product{Status: ProductActive, Colors: []string{"Branco", "Vermelho"},
			SupplierProducts: map[string]string{"Branco": "6167609"}}, []string{"supplier_products"}},
		{"código de cor que não está à venda", Product{Status: ProductActive, Colors: []string{"Branco"},
			SupplierProducts: map[string]string{"Branco": "6167609", "Vermelho": "6170001"}}, []string{"supplier_products"}},
		{"produto com cor e código sem cor", Product{Status: ProductActive, Colors: []string{"Branco"},
			SupplierProducts: map[string]string{NoColor: "6167609"}}, []string{"supplier_products"}},
		{"produto sem cor e código por cor", Product{Status: ProductActive,
			SupplierProducts: map[string]string{"Branco": "6167609"}}, []string{"supplier_products"}},
		{"rascunho com código errado também não passa", Product{Status: ProductDraft,
			SupplierProducts: map[string]string{NoColor: "com espaço"}}, []string{"supplier_products"}},
		{"código vazio", Product{Status: ProductActive, SupplierProducts: map[string]string{NoColor: ""}}, []string{"supplier_products"}},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			assert.Equal(t, tt.fields, fieldsOf(t, tt.p.CheckSupplierProducts()))
		})
	}
}

func TestProduct_Normalize_SupplierProducts(t *testing.T) {
	p := Product{SupplierProducts: map[string]string{" Branco ": " 6167609 "}}
	p.Normalize()
	assert.Equal(t, map[string]string{"Branco": "6167609"}, p.SupplierProducts)

	var none Product
	none.Normalize()
	assert.Nil(t, none.SupplierProducts, "nil continua nil: o PUT mantém os gravados")
}
