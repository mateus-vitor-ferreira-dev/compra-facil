// Package domain contém as entidades do catálogo e os contratos (ports) dos repositórios.
// Não depende de Gin nem de GORM; as regras de negócio entram na tarefa #63.
package domain

import (
	"errors"
	"time"

	"github.com/google/uuid"
)

var (
	ErrNotFound = errors.New("não encontrado")
	// ErrConflict indica violação de unicidade (SKU ou slug já usados).
	ErrConflict = errors.New("conflito com recurso existente")
	// ErrVersionConflict indica que o recurso mudou desde a leitura (If-Match desatualizado).
	ErrVersionConflict = errors.New("versão desatualizada")
	// ErrInvalidReference indica referência a algo que não existe (ex.: category_id).
	ErrInvalidReference = errors.New("referência inválida")
	// ErrInvalidCursor indica um cursor de paginação corrompido ou de outra listagem.
	ErrInvalidCursor = errors.New("cursor inválido")
)

type ProductStatus string

const (
	ProductDraft    ProductStatus = "draft"
	ProductActive   ProductStatus = "active"
	ProductInactive ProductStatus = "inactive"
)

const DefaultCurrency = "BRL"

// ProductImage descreve a foto do produto para o front montar o srcset: a foto em si é quadrada
// (Width x Height) e cada variante é a mesma foto numa largura menor.
type ProductImage struct {
	Width    int            `json:"width"`
	Height   int            `json:"height"`
	Variants []ImageVariant `json:"variants"`
}

type ImageVariant struct {
	Width int    `json:"width"`
	URL   string `json:"url"`
}

// GalleryImage é uma foto a mais do produto, depois da primeira (ImageURL e Image). Image é nula se só a
// URL é conhecida.
type GalleryImage struct {
	URL   string        `json:"url"`
	Image *ProductImage `json:"image,omitempty"`
}

// MaxGalleryImages é quantas fotos o produto tem além da primeira.
const MaxGalleryImages = 9

type Product struct {
	ID          uuid.UUID
	SKU         string
	Name        string
	Description string
	// Brand é a marca (filtro e faceta da listagem); nula se não informada.
	Brand      *string
	CategoryID uuid.UUID
	Category   CategoryRef
	ImageURL   *string
	// Image são as dimensões e as variantes da foto de ImageURL; nulo se só a URL é conhecida.
	Image *ProductImage
	// Gallery são as outras fotos, na ordem da galeria. Num Update, nil mantém as que estão gravadas.
	Gallery []GalleryImage
	// Sizes são os tamanhos que o cliente escolhe na compra (ex.: P, M, G), na ordem de exibição; vazio, o produto
	// não tem tamanho. Preço e estoque continuam do produto (a peça é feita sob demanda). Num Update, nil mantém os
	// que estão gravados.
	Sizes []string
	// Colors são as cores que o cliente escolhe na compra (ex.: Branco, Preto), na ordem de exibição; vazio, o produto
	// não tem cor à escolha. Como o tamanho, preço e estoque são do produto. Num Update, nil mantém as gravadas.
	Colors []string
	// Personalization é o texto que o cliente escreve na compra (ex.: o nome na estampa); nulo, o produto não tem.
	// Num Update, nil mantém a gravada e uma vazia (IsZero) tira.
	Personalization *Personalization
	// SupplierProducts é o código do produto no fornecedor manual (hoje a Montink) de cada cor à venda: a equipe faz o
	// pedido nele. A chave é a cor (NoColor no produto sem cor à escolha); vazio, o produto não tem fornecedor e não
	// fica à venda (ver CheckSupplierProducts). Num Update, nil mantém os gravados e um mapa vazio tira.
	SupplierProducts map[string]string
	PriceCents       int64
	Currency         string
	Status           ProductStatus
	Version          int
	// ActiveCampaign é a campanha vigente de maior desconto, se houver (preenchida na leitura).
	ActiveCampaign *CampaignRef
	// Supplier: o produto vem do feed de um fornecedor de dropshipping (nulo: cadastrado pela loja).
	Supplier  *SupplierInfo
	CreatedAt time.Time
	UpdatedAt time.Time
}

// EffectivePriceCents aplica o desconto da campanha ativa, arredondando a favor do cliente.
// A listagem filtra por preço efetivo no SQL com a mesma fórmula (repository.effectivePriceSQL).
func (p Product) EffectivePriceCents() int64 {
	if p.ActiveCampaign == nil {
		return p.PriceCents
	}
	return p.PriceCents * int64(100-p.ActiveCampaign.DiscountPercent) / 100
}

type Category struct {
	ID       uuid.UUID
	Slug     string
	Name     string
	ParentID *uuid.UUID
	// ActiveProducts são os produtos à venda nela e nas descendentes (preenchido na listagem): a loja esconde a
	// categoria com zero e ela volta sozinha com o primeiro produto ativo (#483).
	ActiveProducts int
	CreatedAt      time.Time
	UpdatedAt      time.Time
}

type CategoryRef struct {
	ID   uuid.UUID
	Slug string
	Name string
}

type Campaign struct {
	ID              uuid.UUID
	Slug            string
	Name            string
	DiscountPercent int
	StartsAt        time.Time
	EndsAt          time.Time
	ProductCount    int
	// Version sobe a cada alteração, inclusive troca de produtos; vai nos eventos (ADR 0002).
	Version   int
	CreatedAt time.Time
	UpdatedAt time.Time
}

// ActiveAt diz se a campanha vale no instante t (início inclusivo, fim exclusivo).
func (c Campaign) ActiveAt(t time.Time) bool {
	return !t.Before(c.StartsAt) && t.Before(c.EndsAt)
}

type CampaignRef struct {
	ID              uuid.UUID
	Name            string
	DiscountPercent int
	EndsAt          time.Time
}
