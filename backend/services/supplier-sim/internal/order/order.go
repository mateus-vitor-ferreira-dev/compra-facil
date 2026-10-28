// Package order guarda os pedidos em memória e conduz o ciclo de vida simulado
// (CREATED → UNSHIPPED → SHIPPED → DELIVERED), emitindo um webhook a cada mudança.
package order

import (
	"context"
	"errors"
	"fmt"
	"hash/fnv"
	"slices"
	"strings"
	"sync"
	"time"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/catalog"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/webhook"
)

// Status da CJ que o simulador percorre.
const (
	StatusCreated   = "CREATED"
	StatusUnshipped = "UNSHIPPED"
	StatusShipped   = "SHIPPED"
	StatusDelivered = "DELIVERED"
	StatusCancelled = "CANCELLED"
)

// Marcadores no remark que mudam o comportamento do pedido (para testar a SAGA da loja).
const (
	MarkReject      = "[recusar]"        // recusa síncrona na criação
	MarkRejectLater = "[recusar-depois]" // aceita e cancela depois do accept delay
	MarkStall       = "[atrasar]"        // fica em UNSHIPPED para sempre
)

// Motivos do cancelamento, enviados em reason.
const (
	ReasonSupplierCancelled = "supplier_cancelled"
	ReasonStoreCancelled    = "store_cancelled"
)

var (
	ErrInvalidParams  = errors.New("invalid params")
	ErrRejected       = errors.New("order rejected by supplier")
	ErrNotFound       = errors.New("order not found")
	ErrAlreadyShipped = errors.New("order already shipped")
)

// OutOfStockError diz qual vid faltou, para a mensagem "Out of stock: <vid>".
type OutOfStockError struct{ VID string }

func (e *OutOfStockError) Error() string { return "Out of stock: " + e.VID }

type Item struct {
	VID       string `json:"vid"`
	Quantity  int    `json:"quantity"`
	UnitPrice string `json:"unitPrice"`
}

type CreateRequest struct {
	OrderNumber          string `json:"orderNumber"`
	ShippingCountryCode  string `json:"shippingCountryCode"`
	ShippingCustomerName string `json:"shippingCustomerName"`
	ShippingAddress      string `json:"shippingAddress"`
	ShippingZip          string `json:"shippingZip"`
	ShippingPhone        string `json:"shippingPhone"`
	Email                string `json:"email"`
	Remark               string `json:"remark"`
	Products             []struct {
		VID      string `json:"vid"`
		Quantity int    `json:"quantity"`
	} `json:"products"`
}

// Order é o pedido como sai no getOrderDetail.
type Order struct {
	OrderID          string    `json:"orderId"`
	OrderNumber      string    `json:"orderNumber"`
	OrderStatus      string    `json:"orderStatus"`
	SubStatus        string    `json:"subStatus"`
	TrackNumber      string    `json:"trackNumber"`
	TrackingProvider string    `json:"trackingProvider"`
	TrackingURL      string    `json:"trackingUrl"`
	Reason           string    `json:"reason"`
	OrderAmount      string    `json:"orderAmount"`
	ProductAmount    string    `json:"productAmount"`
	CreateDate       time.Time `json:"createDate"`
	UpdateDate       time.Time `json:"updateDate"`
	IsSandbox        int       `json:"isSandbox"`
	DisputeID        *string   `json:"disputeId"`
	Remark           string    `json:"remark"`
	Products         []Item    `json:"products"`
}

type Delays struct {
	Accept  time.Duration
	Ship    time.Duration
	Deliver time.Duration
}

// Notifier recebe cada mudança de status (o webhook.Dispatcher em produção).
type Notifier interface {
	Enqueue(p webhook.Params)
}

type Simulator struct {
	stock  *catalog.Stock
	delays Delays
	notify Notifier
	now    func() time.Time

	ctx    context.Context
	cancel context.CancelFunc
	wg     sync.WaitGroup

	mu       sync.Mutex
	byID     map[string]*Order
	byNumber map[string]string // orderNumber → orderId (idempotência da criação)
	seq      int64
}

func NewSimulator(stock *catalog.Stock, delays Delays, notify Notifier) *Simulator {
	ctx, cancel := context.WithCancel(context.Background())
	return &Simulator{
		stock: stock, delays: delays, notify: notify, now: time.Now,
		ctx: ctx, cancel: cancel,
		byID: map[string]*Order{}, byNumber: map[string]string{},
	}
}

// Close para os ciclos de vida em andamento (o estado é só de memória, some com o processo).
func (s *Simulator) Close() {
	s.cancel()
	s.wg.Wait()
}

// Create cria o pedido ou, se o orderNumber já existe, devolve o existente sem duplicar.
// Recusas não guardam nada: repetir o mesmo orderNumber reavalia o pedido.
func (s *Simulator) Create(req CreateRequest) (Order, error) {
	req.OrderNumber = strings.TrimSpace(req.OrderNumber)
	if req.OrderNumber == "" || len(req.Products) == 0 {
		return Order{}, ErrInvalidParams
	}
	for _, p := range req.Products {
		if strings.TrimSpace(p.VID) == "" || p.Quantity <= 0 {
			return Order{}, ErrInvalidParams
		}
	}

	s.mu.Lock()
	defer s.mu.Unlock()
	if id, ok := s.byNumber[req.OrderNumber]; ok {
		return s.snapshot(s.byID[id]), nil
	}
	if strings.Contains(req.Remark, MarkReject) { // "[recusar-depois]" não contém "[recusar]"
		return Order{}, ErrRejected
	}

	items := make([]Item, 0, len(req.Products))
	var total int64
	for _, p := range req.Products {
		if p.Quantity > s.stock.Available(p.VID) {
			return Order{}, &OutOfStockError{VID: p.VID}
		}
		unit := catalog.UnitCostCents(p.VID)
		total += unit * int64(p.Quantity)
		items = append(items, Item{VID: p.VID, Quantity: p.Quantity, UnitPrice: catalog.FormatUSD(unit)})
	}

	s.seq++
	now := s.now().UTC()
	o := &Order{
		OrderID:       fmt.Sprintf("CJ%010d", 1000000000+s.seq),
		OrderNumber:   req.OrderNumber,
		OrderStatus:   StatusCreated,
		OrderAmount:   catalog.FormatUSD(total), // sem frete no simulador: igual ao productAmount
		ProductAmount: catalog.FormatUSD(total),
		CreateDate:    now,
		UpdateDate:    now,
		IsSandbox:     1,
		Remark:        req.Remark,
		Products:      items,
	}
	s.byID[o.OrderID] = o
	s.byNumber[o.OrderNumber] = o.OrderID

	s.wg.Add(1)
	go s.lifecycle(o.OrderID, req.Remark)
	return s.snapshot(o), nil
}

func (s *Simulator) Get(orderID string) (Order, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	o, ok := s.byID[orderID]
	if !ok {
		return Order{}, ErrNotFound
	}
	return s.snapshot(o), nil
}

// List devolve todos os pedidos em ordem de criação (endpoint de depuração).
func (s *Simulator) List() []Order {
	s.mu.Lock()
	defer s.mu.Unlock()
	out := make([]Order, 0, len(s.byID))
	for _, o := range s.byID {
		out = append(out, s.snapshot(o))
	}
	slices.SortFunc(out, func(a, b Order) int { return strings.Compare(a.OrderID, b.OrderID) })
	return out
}

// Cancel atende o deleteOrder: só cancela o que ainda não foi despachado. Cancelar de novo um
// pedido já cancelado devolve sucesso sem novo webhook (o retry da loja não deve falhar).
func (s *Simulator) Cancel(orderID string) (Order, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	o, ok := s.byID[orderID]
	if !ok {
		return Order{}, ErrNotFound
	}
	switch o.OrderStatus {
	case StatusCancelled:
		return s.snapshot(o), nil
	case StatusCreated, StatusUnshipped:
		s.apply(o, func(o *Order) {
			o.OrderStatus, o.SubStatus, o.Reason = StatusCancelled, "", ReasonStoreCancelled
		})
		return s.snapshot(o), nil
	default:
		return Order{}, ErrAlreadyShipped
	}
}

// lifecycle avança o pedido no tempo. Cada passo confere o status sob o lock: um pedido
// cancelado no meio do caminho não avança mais.
func (s *Simulator) lifecycle(orderID, remark string) {
	defer s.wg.Done()
	if !s.sleep(s.delays.Accept) {
		return
	}
	if strings.Contains(remark, MarkRejectLater) {
		s.step(orderID, func(o *Order) {
			o.OrderStatus, o.Reason = StatusCancelled, ReasonSupplierCancelled
		})
		return
	}
	if !s.step(orderID, func(o *Order) { o.OrderStatus, o.SubStatus = StatusUnshipped, "PROCESSING" }) {
		return
	}
	if strings.Contains(remark, MarkStall) {
		return // "preso no fornecedor": nunca despacha
	}
	if !s.sleep(s.delays.Ship) {
		return
	}
	if !s.step(orderID, func(o *Order) {
		o.OrderStatus, o.SubStatus = StatusShipped, ""
		o.TrackNumber = trackNumber(o.OrderID)
		o.TrackingProvider = "Correios"
		o.TrackingURL = "https://t.17track.net/en#nums=" + o.TrackNumber
	}) {
		return
	}
	if !s.sleep(s.delays.Deliver) {
		return
	}
	s.step(orderID, func(o *Order) { o.OrderStatus = StatusDelivered })
}

// step aplica a mudança se o pedido não foi cancelado; devolve false para encerrar o ciclo.
func (s *Simulator) step(orderID string, change func(*Order)) bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	o := s.byID[orderID]
	if o.OrderStatus == StatusCancelled {
		return false
	}
	s.apply(o, change)
	return o.OrderStatus != StatusCancelled
}

// apply muda o pedido e enfileira o webhook ainda sob o lock, para a fila refletir a mesma
// ordem das mudanças (um cancelamento concorrente não passa na frente do passo anterior).
func (s *Simulator) apply(o *Order, change func(*Order)) {
	change(o)
	o.UpdateDate = s.now().UTC()
	if s.notify != nil {
		s.notify.Enqueue(webhook.Params{
			OrderID:          o.OrderID,
			OrderNumber:      o.OrderNumber,
			OrderStatus:      o.OrderStatus,
			SubStatus:        o.SubStatus,
			TrackNumber:      o.TrackNumber,
			TrackingProvider: o.TrackingProvider,
			TrackingURL:      o.TrackingURL,
			Reason:           o.Reason,
			UpdateDate:       o.UpdateDate.Format(time.RFC3339),
		})
	}
}

func (s *Simulator) sleep(d time.Duration) bool {
	t := time.NewTimer(d)
	defer t.Stop()
	select {
	case <-s.ctx.Done():
		return false
	case <-t.C:
		return true
	}
}

// snapshot copia o pedido para fora do lock sem compartilhar o slice de itens.
func (s *Simulator) snapshot(o *Order) Order {
	c := *o
	c.Products = slices.Clone(o.Products)
	return c
}

// trackNumber no formato dos rastreios internacionais que chegam pelos Correios: BR + 9 dígitos + CJ.
func trackNumber(orderID string) string {
	h := fnv.New64a()
	_, _ = h.Write([]byte(orderID))
	return fmt.Sprintf("BR%09dCJ", h.Sum64()%1_000_000_000)
}
