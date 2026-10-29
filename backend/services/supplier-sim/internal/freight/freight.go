// Package freight cota o frete como o freightCalculate do CJ: as opções de envio da China ao CEP, com o
// preço em dólar e o prazo em dias ("12-20"). Determinístico: o mesmo pedido e CEP dão a mesma cotação.
package freight

import (
	"errors"
	"fmt"
	"strings"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/catalog"
)

var ErrInvalid = errors.New("cotação inválida")

type Item struct {
	VID      string `json:"vid"`
	Quantity int    `json:"quantity"`
}

// Option é uma opção de envio, no formato do CJ.
type Option struct {
	LogisticName  string  `json:"logisticName"`
	LogisticPrice float64 `json:"logisticPrice"` // dólares
	LogisticAging string  `json:"logisticAging"` // "12-20" (dias úteis)
}

type service struct {
	name             string
	baseCents        int64 // por pedido
	perKgCents       int64
	minDays, maxDays int
}

// os dois serviços que a loja oferece: o econômico (correio) e o expresso (courier)
var services = []service{
	{"CJPacket Ordinary", 250, 400, 12, 20},
	{"CJ Express", 800, 900, 6, 10},
}

// region: os dias a mais e o fator de preço pelo primeiro dígito do CEP (a região dos Correios)
func region(zip string) (extraDays int, factor float64) {
	switch zip[0] {
	case '0', '1': // SP
		return 0, 1.0
	case '2', '3': // RJ, ES, MG
		return 1, 1.05
	case '8', '9': // PR, SC, RS
		return 2, 1.1
	case '4', '7': // BA, SE, DF, GO, TO, MT, MS
		return 3, 1.2
	default: // 5, 6: Nordeste e Norte
		return 5, 1.35
	}
}

// Quote cota o pedido para o CEP brasileiro. País diferente do Brasil ou pedido sem itens é inválido.
func Quote(country, zip string, items []Item) ([]Option, error) {
	zip = strings.ReplaceAll(zip, "-", "")
	if country != "BR" || len(zip) != 8 || strings.Trim(zip, "0123456789") != "" || len(items) == 0 {
		return nil, fmt.Errorf("%w: país BR, CEP com 8 dígitos e pelo menos um item", ErrInvalid)
	}
	grams := 0
	for _, it := range items {
		if strings.TrimSpace(it.VID) == "" || it.Quantity <= 0 {
			return nil, fmt.Errorf("%w: item sem vid ou quantidade", ErrInvalid)
		}
		grams += catalog.WeightGrams(it.VID) * it.Quantity
	}
	extra, factor := region(zip)
	kg := (int64(grams) + 999) / 1000 // por quilo começado
	out := make([]Option, 0, len(services))
	for _, s := range services {
		cents := int64(float64(s.baseCents+s.perKgCents*kg) * factor)
		out = append(out, Option{
			LogisticName:  s.name,
			LogisticPrice: float64(cents) / 100,
			LogisticAging: fmt.Sprintf("%d-%d", s.minDays+extra, s.maxDays+extra),
		})
	}
	return out, nil
}
