// Package catalog é o catálogo e o estoque do fornecedor simulado: fixos e determinísticos,
// para que os testes da loja possam contar com os mesmos números a cada subida.
package catalog

import (
	"errors"
	"fmt"
	"hash/fnv"
	"net/url"
	"sync"
)

// ErrUnknownProduct: o vid não está no feed.
var ErrUnknownProduct = errors.New("produto desconhecido")

// Product segue os campos do item de /product/list da CJ que a loja usa.
type Product struct {
	PID           string `json:"pid"`
	VID           string `json:"vid"`
	ProductSKU    string `json:"productSku"`
	ProductNameEn string `json:"productNameEn"`
	SellPrice     string `json:"sellPrice"`
	CategoryName  string `json:"categoryName"`
	ProductImage  string `json:"productImage"`
	ProductWeight int    `json:"productWeight"` // gramas
}

type seed struct {
	name   string
	cents  int64
	weight int
}

// categorias e itens em ordem fixa: a paginação devolve sempre a mesma página
var seeds = []struct {
	category string
	items    []seed
}{
	{"Headphones", []seed{
		{"Wireless Bluetooth Headphones", 1240, 210},
		{"Noise Cancelling Over-Ear Headphones", 3290, 280},
		{"Sports In-Ear Earbuds", 690, 40},
		{"Gaming Headset with Microphone", 1890, 320},
		{"Foldable Kids Headphones", 850, 180},
		{"True Wireless Earbuds Charging Case", 1450, 60},
	}},
	{"Mouse", []seed{
		{"Wireless Optical Mouse", 520, 80},
		{"RGB Gaming Mouse 7200 DPI", 1130, 110},
		{"Vertical Ergonomic Mouse", 980, 120},
		{"Silent Click Mini Mouse", 410, 60},
		{"Rechargeable Bluetooth Mouse", 760, 90},
		{"Trackball Mouse", 2150, 190},
	}},
	{"Keyboard", []seed{
		{"Mechanical Keyboard Blue Switch", 2790, 850},
		{"Wireless Slim Keyboard", 1390, 420},
		{"60% Compact Mechanical Keyboard", 3150, 600},
		{"Keyboard and Mouse Combo", 1690, 700},
		{"Foldable Bluetooth Keyboard", 1250, 200},
		{"Numeric Keypad USB", 480, 120},
	}},
	{"Lipstick", []seed{
		{"Matte Liquid Lipstick Red", 310, 20},
		{"Moisturizing Lip Balm Tint", 240, 15},
		{"Long Lasting Nude Lipstick", 290, 20},
		{"Velvet Lipstick Set 6 Colors", 870, 120},
		{"Glossy Lip Oil", 270, 18},
		{"Waterproof Lip Liner", 190, 10},
	}},
	{"Frying Pan", []seed{
		{"Non-Stick Frying Pan 24cm", 1590, 900},
		{"Cast Iron Skillet 26cm", 2490, 2300},
		{"Ceramic Coated Frying Pan 28cm", 1990, 1100},
		{"Stainless Steel Saute Pan", 2690, 1500},
		{"Mini Egg Frying Pan 16cm", 690, 400},
		{"Grill Pan with Folding Handle", 1790, 1200},
	}},
}

// o catálogo muda pelas rotas /sim (custo novo, produto que saiu do feed); o resto só lê
var (
	mu       sync.Mutex
	products []Product
	byVID    map[string]int64
)

func init() { Reset() }

// Reset volta o catálogo ao inicial: os 30 produtos, com os custos de fábrica.
func Reset() {
	mu.Lock()
	defer mu.Unlock()
	products = products[:0]
	byVID = map[string]int64{}
	n := 0
	for ci, cat := range seeds {
		for _, s := range cat.items {
			n++
			p := Product{
				PID:           fmt.Sprintf("SIM-P-%03d", n),
				VID:           fmt.Sprintf("SIM-V-%03d", n),
				ProductSKU:    fmt.Sprintf("CJSIM%02d%03d", ci+1, n),
				ProductNameEn: s.name,
				SellPrice:     FormatUSD(s.cents),
				CategoryName:  cat.category,
				ProductImage:  "https://placehold.co/600x600/png?text=" + url.QueryEscape(s.name),
				ProductWeight: s.weight,
			}
			products = append(products, p)
			byVID[p.VID] = s.cents
		}
	}
}

// SetCost muda o custo de um produto (o fornecedor reajustou): aparece no feed e nos pedidos novos.
func SetCost(vid string, cents int64) error {
	mu.Lock()
	defer mu.Unlock()
	for i := range products {
		if products[i].VID == vid {
			products[i].SellPrice = FormatUSD(cents)
			byVID[vid] = cents
			return nil
		}
	}
	return ErrUnknownProduct
}

// Remove tira o produto do feed (o fornecedor deixou de vender). Pedidos com o vid seguem com o
// último custo, como qualquer vid fora do catálogo.
func Remove(vid string) error {
	mu.Lock()
	defer mu.Unlock()
	for i := range products {
		if products[i].VID == vid {
			products = append(products[:i], products[i+1:]...)
			return nil
		}
	}
	return ErrUnknownProduct
}

// Page devolve uma página (pageNum começa em 1) e o total de itens.
func Page(pageNum, pageSize int) ([]Product, int) {
	mu.Lock()
	defer mu.Unlock()
	total := len(products)
	start := (pageNum - 1) * pageSize
	if start >= total {
		return []Product{}, total
	}
	end := min(start+pageSize, total)
	out := make([]Product, end-start)
	copy(out, products[start:end])
	return out, total
}

// Stock informa o estoque de um vid. Qualquer vid é aceito (hoje a loja manda o próprio SKU),
// com quantidade derivada do hash: estável entre subidas, entre 5 e 200.
// SetStock fixa a disponibilidade de um vid (rota /sim), por cima da derivada do hash.
type Stock struct {
	outOfStock map[string]bool

	mu       sync.Mutex
	override map[string]int
}

func NewStock(outOfStock map[string]bool) *Stock {
	return &Stock{outOfStock: outOfStock, override: map[string]int{}}
}

func (s *Stock) Available(vid string) int {
	s.mu.Lock()
	n, fixed := s.override[vid]
	s.mu.Unlock()
	if fixed {
		return n
	}
	if s.outOfStock[vid] {
		return 0
	}
	return int(5 + hash(vid)%196)
}

// SetStock fixa a disponibilidade do vid (0 = esgotou no fornecedor).
func (s *Stock) SetStock(vid string, n int) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.override[vid] = max(n, 0)
}

// ResetStock volta todos os vids ao estoque derivado do hash.
func (s *Stock) ResetStock() {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.override = map[string]int{}
}

// UnitCostCents é o custo unitário em centavos de dólar: o sellPrice para vids do catálogo,
// um valor derivado do hash (US$ 3,00 a 60,00) para os demais.
func UnitCostCents(vid string) int64 {
	mu.Lock()
	defer mu.Unlock()
	if c, ok := byVID[vid]; ok {
		return c
	}
	return int64(300 + hash("cost:"+vid)%5701)
}

// FormatUSD formata centavos como a CJ manda valores: string decimal com 2 casas.
func FormatUSD(cents int64) string {
	return fmt.Sprintf("%d.%02d", cents/100, cents%100)
}

func hash(s string) uint64 {
	h := fnv.New64a()
	_, _ = h.Write([]byte(s))
	return h.Sum64()
}
