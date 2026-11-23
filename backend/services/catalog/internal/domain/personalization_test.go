package domain

import (
	"strings"
	"testing"

	"github.com/stretchr/testify/assert"
)

func TestProduct_Validate_Personalization(t *testing.T) {
	ok := Personalization{Label: "Nome", MaxLength: 20, Template: "nome-classico"}
	tests := []struct {
		name   string
		change func(*Personalization)
		field  string
	}{
		{"válida", func(*Personalization) {}, ""},
		{"arte com variante", func(c *Personalization) { c.Template = "nome-classico:mae" }, ""},
		{"rótulo vazio", func(c *Personalization) { c.Label = "" }, "personalization.label"},
		{"rótulo longo", func(c *Personalization) { c.Label = strings.Repeat("a", MaxPersonalizationLabel+1) }, "personalization.label"},
		{"tamanho zero", func(c *Personalization) { c.MaxLength = 0 }, "personalization.max_length"},
		{"tamanho acima do máximo", func(c *Personalization) { c.MaxLength = MaxPersonalizationLength + 1 }, "personalization.max_length"},
		{"arte vazia", func(c *Personalization) { c.Template = "" }, "personalization.template"},
		{"arte com maiúscula", func(c *Personalization) { c.Template = "Nome" }, "personalization.template"},
		{"arte com duas variantes", func(c *Personalization) { c.Template = "a:b:c" }, "personalization.template"},
		{"arte longa", func(c *Personalization) { c.Template = strings.Repeat("a", MaxPersonalizationTmpl+1) }, "personalization.template"},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			p := validProduct()
			c := ok
			tt.change(&c)
			p.Personalization = &c
			if tt.field == "" {
				assert.NoError(t, p.Validate())
			} else {
				assert.Equal(t, []string{tt.field}, fieldsOf(t, p.Validate()))
			}
		})
	}

	p := validProduct()
	p.Personalization = &Personalization{}
	assert.NoError(t, p.Validate(), "vazia é o pedido para tirar, não um erro")
}

func TestNormalizePersonalizationText(t *testing.T) {
	assert.Equal(t, "Ana Maria", NormalizePersonalizationText("  Ana \t  Maria \n"))
	assert.Empty(t, NormalizePersonalizationText("   "))
}

func TestProduct_AcceptsPersonalization(t *testing.T) {
	sem := Product{}
	assert.True(t, sem.AcceptsPersonalization(""))
	assert.False(t, sem.AcceptsPersonalization("Ana"), "produto sem personalização recusa texto")

	opcional := Product{Personalization: &Personalization{Label: "Nome", MaxLength: 12, Template: "nome-classico"}}
	assert.True(t, opcional.AcceptsPersonalization(""), "opcional aceita vazio")
	assert.True(t, opcional.AcceptsPersonalization("João"), "acento")
	assert.True(t, opcional.AcceptsPersonalization("Sr. D'Ávila"), "pontuação simples")
	assert.True(t, opcional.AcceptsPersonalization("Desde 2019!"), "algarismo e exclamação")
	assert.True(t, opcional.AcceptsPersonalization("Çéü-ñ/?,& 12"), "doze caracteres, no limite")
	assert.False(t, opcional.AcceptsPersonalization("Ana Carolina S"), "acima do máximo")
	assert.False(t, opcional.AcceptsPersonalization("Ana 🎅"), "emoji")
	assert.False(t, opcional.AcceptsPersonalization("Анна"), "outro alfabeto")
	assert.False(t, opcional.AcceptsPersonalization("a#b"), "pontuação fora da lista")

	obrigatoria := Product{Personalization: &Personalization{Label: "Nome", MaxLength: 10, Required: true, Template: "nome-classico"}}
	assert.False(t, obrigatoria.AcceptsPersonalization(""), "obrigatória exige texto")
	assert.True(t, obrigatoria.AcceptsPersonalization("Bia"))
}
