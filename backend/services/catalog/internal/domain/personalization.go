package domain

import (
	"regexp"
	"strings"
	"unicode"
	"unicode/utf8"
)

// Limites da personalização: o rótulo do campo, o tamanho do texto e o id da arte.
const (
	MaxPersonalizationLabel  = 30
	MaxPersonalizationLength = 40
	MaxPersonalizationTmpl   = 40
	// MaxPersonalizationInput é o texto mais longo que o ValidateItems aceita ler (antes de normalizar); acima disso a
	// requisição é malformada, não um item inválido.
	MaxPersonalizationInput = 200
)

// Personalization é o texto que o cliente escreve na compra, impresso na arte do produto (ex.: o nome na camiseta).
// Template é o id da arte que recebe o texto (ex.: "nome-classico:mae"), resolvido pelo painel na hora de gerar o
// arquivo de impressão; o catalog só guarda e confere o formato.
type Personalization struct {
	Label     string `json:"label"`
	MaxLength int    `json:"max_length"`
	Required  bool   `json:"required"`
	Template  string `json:"template"`
}

// IsZero diz se a personalização veio vazia ({} no PUT): é o pedido para tirar a personalização do produto.
func (p *Personalization) IsZero() bool {
	return p != nil && *p == Personalization{}
}

// templatePattern: um slug, e opcionalmente ":" e outro slug (a variante da arte, ex.: nome-classico:mae).
var templatePattern = regexp.MustCompile(`^[a-z0-9]+(-[a-z0-9]+)*(:[a-z0-9]+(-[a-z0-9]+)*)?$`)

func (p *Product) validatePersonalization(v *validator) {
	c := p.Personalization
	if c == nil || c.IsZero() {
		return
	}
	v.length("personalization.label", c.Label, 1, MaxPersonalizationLabel)
	v.check(c.MaxLength >= 1 && c.MaxLength <= MaxPersonalizationLength, "personalization.max_length",
		"deve estar entre 1 e %d", MaxPersonalizationLength)
	v.check(len(c.Template) <= MaxPersonalizationTmpl && templatePattern.MatchString(c.Template), "personalization.template",
		"deve ser um id de arte de até %d caracteres (ex.: nome-classico ou nome-classico:mae)", MaxPersonalizationTmpl)
}

// NormalizePersonalizationText tira os espaços das pontas e junta os de dentro num só.
func NormalizePersonalizationText(text string) string {
	return strings.Join(strings.Fields(text), " ")
}

// personalizationRune: letras do alfabeto latino (com acento), algarismos, o espaço e a pontuação simples que a
// fonte da arte imprime. Emoji e outros alfabetos ficam de fora.
func personalizationRune(r rune) bool {
	return r == ' ' || strings.ContainsRune(".,'-&!?/", r) || (r >= '0' && r <= '9') || unicode.Is(unicode.Latin, r)
}

// AcceptsPersonalization diz se o texto (já normalizado) vale para o produto: o produto sem personalização só aceita
// o vazio; o com personalização exige o texto quando é obrigatória, até MaxLength caracteres e só com os caracteres
// que a arte imprime (sem emoji).
func (p *Product) AcceptsPersonalization(text string) bool {
	c := p.Personalization
	if c == nil || c.IsZero() {
		return text == ""
	}
	if text == "" {
		return !c.Required
	}
	if utf8.RuneCountInString(text) > c.MaxLength {
		return false
	}
	for _, r := range text {
		if !personalizationRune(r) {
			return false
		}
	}
	return true
}
