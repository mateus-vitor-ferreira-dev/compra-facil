package domain

import (
	"fmt"
	"net/url"
	"regexp"
	"strings"
	"unicode/utf8"
)

// FieldError aponta um campo inválido; Field usa o nome do JSON do contrato (snake_case).
type FieldError struct {
	Field   string
	Message string
}

// ValidationError agrupa os campos inválidos de uma operação; o handler responde 422.
type ValidationError struct {
	Fields []FieldError
}

func (e *ValidationError) Error() string {
	parts := make([]string, len(e.Fields))
	for i, f := range e.Fields {
		parts[i] = f.Field + ": " + f.Message
	}
	return "dados inválidos: " + strings.Join(parts, "; ")
}

// validator acumula erros para devolver todos de uma vez, não só o primeiro.
type validator struct{ fields []FieldError }

func (v *validator) add(field, format string, args ...any) {
	v.fields = append(v.fields, FieldError{Field: field, Message: fmt.Sprintf(format, args...)})
}

func (v *validator) check(ok bool, field, format string, args ...any) {
	if !ok {
		v.add(field, format, args...)
	}
}

func (v *validator) length(field, value string, minLen, maxLen int) {
	n := utf8.RuneCountInString(strings.TrimSpace(value))
	v.check(n >= minLen && n <= maxLen, field, "deve ter entre %d e %d caracteres", minLen, maxLen)
}

func (v *validator) err() error {
	if len(v.fields) == 0 {
		return nil
	}
	return &ValidationError{Fields: v.fields}
}

// Invalid cria um ValidationError de um campo só.
func Invalid(field, message string) error {
	return &ValidationError{Fields: []FieldError{{Field: field, Message: message}}}
}

const (
	MaxPriceCents      = 100_000_000 // R$ 1 milhão
	MaxBrandLength     = 80
	MinDiscountPercent = 1
	MaxDiscountPercent = 90
	MaxCampaignItems   = 1000
)

var (
	skuPattern  = regexp.MustCompile(`^[A-Z0-9][A-Z0-9-]{2,31}$`)
	slugPattern = regexp.MustCompile(`^[a-z0-9]+(-[a-z0-9]+)*$`)
)

func (s ProductStatus) Valid() bool {
	switch s {
	case ProductDraft, ProductActive, ProductInactive:
		return true
	}
	return false
}

// Validate confere as regras de cadastro do produto (não consulta o banco).
func (p *Product) Validate() error {
	var v validator
	v.check(skuPattern.MatchString(p.SKU), "sku", "use 3 a 32 caracteres: letras maiúsculas, dígitos e hífen")
	v.length("name", p.Name, 3, 200)
	v.check(utf8.RuneCountInString(p.Description) <= 5000, "description", "deve ter no máximo 5000 caracteres")
	if p.Brand != nil {
		v.length("brand", *p.Brand, 1, MaxBrandLength)
	}
	v.check(p.PriceCents >= 1 && p.PriceCents <= MaxPriceCents, "price_cents", "deve estar entre 1 e %d", MaxPriceCents)
	v.check(p.Status.Valid(), "status", "deve ser draft, active ou inactive")
	if p.ImageURL != nil {
		v.check(isHTTPURL(*p.ImageURL), "image_url", "deve ser uma URL http(s) absoluta")
	}
	return v.err()
}

// Normalize remove espaços das pontas antes de validar e gravar.
func (p *Product) Normalize() {
	p.SKU = strings.TrimSpace(p.SKU)
	p.Name = strings.TrimSpace(p.Name)
	p.Description = strings.TrimSpace(p.Description)
	if p.Brand != nil {
		trimmed := strings.TrimSpace(*p.Brand)
		if trimmed == "" {
			p.Brand = nil
		} else {
			p.Brand = &trimmed
		}
	}
	if p.ImageURL != nil {
		trimmed := strings.TrimSpace(*p.ImageURL)
		if trimmed == "" {
			p.ImageURL = nil
		} else {
			p.ImageURL = &trimmed
		}
	}
}

// PriceChanged diz se a atualização muda o preço de tabela (gera o evento price_changed).
func (p Product) PriceChanged(before Product) bool {
	return p.PriceCents != before.PriceCents || p.Currency != before.Currency
}

func (c *Category) Validate() error {
	var v validator
	v.check(len(c.Slug) <= 80 && slugPattern.MatchString(c.Slug), "slug", "use minúsculas, dígitos e hífen (até 80)")
	v.length("name", c.Name, 2, 120)
	if c.ParentID != nil {
		v.check(*c.ParentID != c.ID, "parent_id", "a categoria não pode ser pai de si mesma")
	}
	return v.err()
}

func (c *Campaign) Validate() error {
	var v validator
	v.check(len(c.Slug) <= 80 && slugPattern.MatchString(c.Slug), "slug", "use minúsculas, dígitos e hífen (até 80)")
	v.length("name", c.Name, 3, 120)
	v.check(c.DiscountPercent >= MinDiscountPercent && c.DiscountPercent <= MaxDiscountPercent,
		"discount_percent", "deve estar entre %d e %d", MinDiscountPercent, MaxDiscountPercent)
	v.check(!c.StartsAt.IsZero(), "starts_at", "é obrigatório")
	v.check(!c.EndsAt.IsZero(), "ends_at", "é obrigatório")
	if !c.StartsAt.IsZero() && !c.EndsAt.IsZero() {
		v.check(c.EndsAt.After(c.StartsAt), "ends_at", "deve ser posterior a starts_at")
	}
	return v.err()
}

func isHTTPURL(raw string) bool {
	u, err := url.Parse(raw)
	return err == nil && (u.Scheme == "http" || u.Scheme == "https") && u.Host != ""
}
