// Package payment é o provedor de pagamento simulado, no formato do Mercado Pago (subconjunto que a loja
// usa): tokens de cartão, PIX com QR que expira, cartão com autorização e captura, e estornos. O estado é
// só de memória; o comportamento é determinístico (o resultado do cartão sai do nome do titular, como nos
// cartões de teste do Mercado Pago).
package payment

import (
	"errors"
	"fmt"
	"strings"
	"sync"
	"time"
)

var (
	ErrNotFound       = errors.New("payment not found")
	ErrInvalidToken   = errors.New("invalid card token")
	ErrInvalidCard    = errors.New("invalid card")
	ErrInvalidRequest = errors.New("invalid request")
	ErrInvalidState   = errors.New("invalid payment state")
	ErrRefundTooLarge = errors.New("refund amount exceeds the available balance")
)

// Status e status_detail do Mercado Pago.
const (
	StatusPending    = "pending"
	StatusApproved   = "approved"
	StatusAuthorized = "authorized"
	StatusInProcess  = "in_process"
	StatusRejected   = "rejected"
	StatusCancelled  = "cancelled"
	StatusRefunded   = "refunded"

	DetailWaitingTransfer   = "pending_waiting_transfer"
	DetailAccredited        = "accredited"
	DetailPendingCapture    = "pending_capture"
	DetailPendingReview     = "pending_review_manual"
	DetailInsufficientFunds = "cc_rejected_insufficient_amount"
	DetailBadSecurityCode   = "cc_rejected_bad_filled_security_code"
	DetailHighRisk          = "cc_rejected_high_risk"
	DetailOtherReason       = "cc_rejected_other_reason"
	DetailExpired           = "expired"
	DetailByCollector       = "by_collector"
	DetailPartiallyRefunded = "partially_refunded"
	DetailRefunded          = "refunded"
)

// CardToken é o cartão tokenizado: a loja só vê o token e os 4 últimos dígitos.
type CardToken struct {
	ID              string `json:"id"`
	LastFourDigits  string `json:"last_four_digits"`
	FirstSixDigits  string `json:"first_six_digits"`
	PaymentMethodID string `json:"payment_method_id"`
	CardholderName  string `json:"-"`
	used            bool
}

// TransactionData é o PIX: o copia-e-cola, o QR em PNG (base64) e a página de pagamento.
type TransactionData struct {
	QRCode       string `json:"qr_code"`
	QRCodeBase64 string `json:"qr_code_base64"`
	TicketURL    string `json:"ticket_url"`
}

type PointOfInteraction struct {
	Type            string           `json:"type"`
	TransactionData *TransactionData `json:"transaction_data,omitempty"`
}

type Card struct {
	LastFourDigits string `json:"last_four_digits"`
	FirstSixDigits string `json:"first_six_digits"`
}

type Refund struct {
	ID          int64     `json:"id"`
	PaymentID   int64     `json:"payment_id"`
	Amount      Amount    `json:"amount"`
	Status      string    `json:"status"`
	DateCreated time.Time `json:"date_created"`
}

// Payment segue os campos do /v1/payments do Mercado Pago que a loja usa.
type Payment struct {
	ID                 int64               `json:"id"`
	Status             string              `json:"status"`
	StatusDetail       string              `json:"status_detail"`
	PaymentMethodID    string              `json:"payment_method_id"`
	PaymentTypeID      string              `json:"payment_type_id"`
	TransactionAmount  Amount              `json:"transaction_amount"`
	TransactionRefund  Amount              `json:"transaction_amount_refunded"`
	Installments       int                 `json:"installments"`
	Captured           bool                `json:"captured"`
	Description        string              `json:"description"`
	ExternalReference  string              `json:"external_reference"`
	PayerEmail         string              `json:"-"`
	NotificationURL    string              `json:"notification_url,omitempty"`
	DateCreated        time.Time           `json:"date_created"`
	DateApproved       *time.Time          `json:"date_approved"`
	DateLastUpdated    time.Time           `json:"date_last_updated"`
	DateOfExpiration   *time.Time          `json:"date_of_expiration"`
	Card               *Card               `json:"card,omitempty"`
	PointOfInteraction *PointOfInteraction `json:"point_of_interaction,omitempty"`
	Refunds            []Refund            `json:"refunds"`
}

// CreateRequest é o corpo do POST /v1/payments.
type CreateRequest struct {
	TransactionAmount Amount `json:"transaction_amount"`
	Description       string `json:"description"`
	PaymentMethodID   string `json:"payment_method_id"`
	Token             string `json:"token"`
	Installments      int    `json:"installments"`
	// Capture nulo é true (padrão do Mercado Pago); false só autoriza
	Capture           *bool      `json:"capture"`
	ExternalReference string     `json:"external_reference"`
	NotificationURL   string     `json:"notification_url"`
	DateOfExpiration  *time.Time `json:"date_of_expiration"`
	Payer             struct {
		Email string `json:"email"`
	} `json:"payer"`
}

// Notifier recebe cada pagamento que mudou (o webhook em produção).
type Notifier interface {
	PaymentChanged(p Payment)
}

type Options struct {
	PIXExpiration time.Duration
	// ReviewDelay: o cartão "CONT" fica em análise (in_process) por este tempo e depois é aprovado.
	ReviewDelay time.Duration
	// TicketURL monta a página "pague o PIX" do simulador para um pagamento.
	TicketURL func(id int64) string
}

// Provider guarda os pagamentos e aplica as regras. Seguro para uso concorrente.
type Provider struct {
	opts   Options
	notify Notifier
	now    func() time.Time

	mu           sync.Mutex
	seq          int64
	payments     map[int64]*Payment
	tokens       map[string]*CardToken
	byKey        map[string]int64  // X-Idempotency-Key → pagamento
	refundsByKey map[string]Refund // X-Idempotency-Key → estorno
	timers       []*time.Timer
}

func NewProvider(opts Options, notify Notifier) *Provider {
	if opts.PIXExpiration <= 0 {
		opts.PIXExpiration = 30 * time.Minute
	}
	if opts.TicketURL == nil {
		opts.TicketURL = func(id int64) string { return fmt.Sprintf("http://localhost:8092/sim/pix/%d", id) }
	}
	p := &Provider{opts: opts, notify: notify, now: time.Now}
	p.resetLocked()
	return p
}

func (p *Provider) resetLocked() {
	for _, t := range p.timers {
		t.Stop()
	}
	p.seq, p.timers = 1_000_000_000, nil
	p.payments, p.tokens = map[int64]*Payment{}, map[string]*CardToken{}
	p.byKey, p.refundsByKey = map[string]int64{}, map[string]Refund{}
}

// Reset apaga tudo (rota /sim/reset).
func (p *Provider) Reset() {
	p.mu.Lock()
	defer p.mu.Unlock()
	p.resetLocked()
}

// Close para os timers (desligamento e testes).
func (p *Provider) Close() {
	p.mu.Lock()
	defer p.mu.Unlock()
	for _, t := range p.timers {
		t.Stop()
	}
}

// TokenizeCard valida o cartão (Luhn, validade, CVV) e devolve o token de uso único.
func (p *Provider) TokenizeCard(number, cvv, holder string, month, year int) (CardToken, error) {
	digits := strings.ReplaceAll(strings.ReplaceAll(number, " ", ""), "-", "")
	brand, ok := brandOf(digits)
	if !ok || !luhn(digits) {
		return CardToken{}, fmt.Errorf("%w: número", ErrInvalidCard)
	}
	if len(cvv) < 3 || len(cvv) > 4 || strings.Trim(cvv, "0123456789") != "" {
		return CardToken{}, fmt.Errorf("%w: código de segurança", ErrInvalidCard)
	}
	now := p.now()
	if month < 1 || month > 12 || year < now.Year() || (year == now.Year() && month < int(now.Month())) {
		return CardToken{}, fmt.Errorf("%w: validade", ErrInvalidCard)
	}
	if strings.TrimSpace(holder) == "" {
		return CardToken{}, fmt.Errorf("%w: titular", ErrInvalidCard)
	}
	p.mu.Lock()
	defer p.mu.Unlock()
	t := &CardToken{ID: randomHex(16), LastFourDigits: digits[len(digits)-4:], FirstSixDigits: digits[:6],
		PaymentMethodID: brand, CardholderName: strings.ToUpper(strings.TrimSpace(holder))}
	p.tokens[t.ID] = t
	return *t, nil
}

// Create cria o pagamento; a mesma chave de idempotência devolve o mesmo pagamento, sem cobrar de novo.
func (p *Provider) Create(key string, req CreateRequest) (Payment, error) {
	if strings.TrimSpace(key) == "" || req.TransactionAmount.Cents <= 0 {
		return Payment{}, fmt.Errorf("%w: X-Idempotency-Key e transaction_amount são obrigatórios", ErrInvalidRequest)
	}
	p.mu.Lock()
	defer p.mu.Unlock()
	if id, ok := p.byKey[key]; ok {
		return *p.payments[id], nil
	}
	now := p.now().UTC()
	p.seq++
	pay := &Payment{
		ID: p.seq, PaymentMethodID: req.PaymentMethodID, TransactionAmount: req.TransactionAmount,
		Description: req.Description, ExternalReference: req.ExternalReference, PayerEmail: req.Payer.Email,
		NotificationURL: req.NotificationURL, DateCreated: now, DateLastUpdated: now, Installments: 1, Refunds: []Refund{},
	}
	switch req.PaymentMethodID {
	case "pix":
		pay.PaymentTypeID = "bank_transfer"
		expires := now.Add(p.opts.PIXExpiration)
		if req.DateOfExpiration != nil && req.DateOfExpiration.After(now) {
			expires = req.DateOfExpiration.UTC()
		}
		pay.DateOfExpiration = &expires
		pay.Status, pay.StatusDetail = StatusPending, DetailWaitingTransfer
		code := pixCode(pay.ID, req.TransactionAmount)
		png, err := qrPNG(code)
		if err != nil {
			return Payment{}, err
		}
		pay.PointOfInteraction = &PointOfInteraction{Type: "OPENPLATFORM", TransactionData: &TransactionData{
			QRCode: code, QRCodeBase64: png, TicketURL: p.opts.TicketURL(pay.ID)}}
		p.schedule(time.Until(expires), func() { p.expire(pay.ID) })
	default:
		tok, ok := p.tokens[req.Token]
		if !ok || tok.used || tok.PaymentMethodID != req.PaymentMethodID {
			return Payment{}, ErrInvalidToken
		}
		tok.used = true
		pay.PaymentTypeID = "credit_card"
		pay.Card = &Card{LastFourDigits: tok.LastFourDigits, FirstSixDigits: tok.FirstSixDigits}
		if req.Installments > 0 {
			pay.Installments = req.Installments
		}
		capture := req.Capture == nil || *req.Capture
		p.decideCard(pay, tok.CardholderName, capture, now)
	}
	p.payments[pay.ID] = pay
	p.byKey[key] = pay.ID
	p.changed(pay)
	return *pay, nil
}

// decideCard aplica a regra do titular (os cartões de teste do Mercado Pago).
func (p *Provider) decideCard(pay *Payment, holder string, capture bool, now time.Time) {
	reject := func(detail string) { pay.Status, pay.StatusDetail = StatusRejected, detail }
	switch strings.Fields(holder + " APRO")[0] {
	case "FUND":
		reject(DetailInsufficientFunds)
	case "SECU":
		reject(DetailBadSecurityCode)
	case "FRAU":
		reject(DetailHighRisk)
	case "OTHE":
		reject(DetailOtherReason)
	case "CONT":
		pay.Status, pay.StatusDetail = StatusInProcess, DetailPendingReview
		id := pay.ID
		p.schedule(p.opts.ReviewDelay, func() { p.approveReview(id, capture) })
	default:
		p.approveCard(pay, capture, now)
	}
}

func (p *Provider) approveCard(pay *Payment, capture bool, now time.Time) {
	pay.DateApproved = &now
	if capture {
		pay.Status, pay.StatusDetail, pay.Captured = StatusApproved, DetailAccredited, true
	} else {
		pay.Status, pay.StatusDetail = StatusAuthorized, DetailPendingCapture
	}
}

func (p *Provider) approveReview(id int64, capture bool) {
	p.mu.Lock()
	defer p.mu.Unlock()
	pay, ok := p.payments[id]
	if !ok || pay.Status != StatusInProcess {
		return
	}
	now := p.now().UTC()
	p.approveCard(pay, capture, now)
	pay.DateLastUpdated = now
	p.changed(pay)
}

// PayPIX é o banco do cliente pagando o QR (rota /sim): o PIX pendente fica aprovado.
func (p *Provider) PayPIX(id int64) (Payment, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	pay, ok := p.payments[id]
	if !ok {
		return Payment{}, ErrNotFound
	}
	if pay.PaymentMethodID != "pix" || pay.Status != StatusPending {
		return Payment{}, fmt.Errorf("%w: %s", ErrInvalidState, pay.Status)
	}
	now := p.now().UTC()
	pay.Status, pay.StatusDetail, pay.Captured, pay.DateApproved, pay.DateLastUpdated = StatusApproved, DetailAccredited, true, &now, now
	p.changed(pay)
	return *pay, nil
}

func (p *Provider) expire(id int64) {
	p.mu.Lock()
	defer p.mu.Unlock()
	pay, ok := p.payments[id]
	if !ok || pay.Status != StatusPending {
		return
	}
	pay.Status, pay.StatusDetail, pay.DateLastUpdated = StatusCancelled, DetailExpired, p.now().UTC()
	p.changed(pay)
}

func (p *Provider) Get(id int64) (Payment, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	pay, ok := p.payments[id]
	if !ok {
		return Payment{}, ErrNotFound
	}
	return *pay, nil
}

// List devolve todos (rota /sim, para depurar), do mais novo para o mais velho.
func (p *Provider) List() []Payment {
	p.mu.Lock()
	defer p.mu.Unlock()
	out := make([]Payment, 0, len(p.payments))
	for id := p.seq; id > 1_000_000_000; id-- {
		if pay, ok := p.payments[id]; ok {
			out = append(out, *pay)
		}
	}
	return out
}

// Capture efetiva a autorização (PUT capture=true). Repetir num capturado devolve o mesmo.
func (p *Provider) Capture(id int64) (Payment, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	pay, ok := p.payments[id]
	if !ok {
		return Payment{}, ErrNotFound
	}
	if pay.Status == StatusApproved && pay.Captured {
		return *pay, nil
	}
	if pay.Status != StatusAuthorized {
		return Payment{}, fmt.Errorf("%w: %s", ErrInvalidState, pay.Status)
	}
	pay.Status, pay.StatusDetail, pay.Captured, pay.DateLastUpdated = StatusApproved, DetailAccredited, true, p.now().UTC()
	p.changed(pay)
	return *pay, nil
}

// Cancel desfaz a autorização (ou o PIX ainda não pago): nada foi cobrado. Repetir devolve o mesmo.
func (p *Provider) Cancel(id int64) (Payment, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	pay, ok := p.payments[id]
	if !ok {
		return Payment{}, ErrNotFound
	}
	if pay.Status == StatusCancelled {
		return *pay, nil
	}
	if pay.Status != StatusAuthorized && pay.Status != StatusPending && pay.Status != StatusInProcess {
		return Payment{}, fmt.Errorf("%w: %s", ErrInvalidState, pay.Status)
	}
	pay.Status, pay.StatusDetail, pay.DateLastUpdated = StatusCancelled, DetailByCollector, p.now().UTC()
	p.changed(pay)
	return *pay, nil
}

// Refund devolve amount (nil = o que falta) de um pagamento capturado; a mesma chave devolve o mesmo estorno.
func (p *Provider) Refund(id int64, key string, amount *Amount) (Refund, error) {
	if strings.TrimSpace(key) == "" {
		return Refund{}, fmt.Errorf("%w: X-Idempotency-Key é obrigatório", ErrInvalidRequest)
	}
	p.mu.Lock()
	defer p.mu.Unlock()
	if r, ok := p.refundsByKey[key]; ok {
		return r, nil
	}
	pay, ok := p.payments[id]
	if !ok {
		return Refund{}, ErrNotFound
	}
	if pay.Status != StatusApproved || !pay.Captured {
		return Refund{}, fmt.Errorf("%w: %s", ErrInvalidState, pay.Status)
	}
	left := pay.TransactionAmount.Cents - pay.TransactionRefund.Cents
	cents := left
	if amount != nil {
		cents = amount.Cents
	}
	if cents <= 0 || cents > left {
		return Refund{}, ErrRefundTooLarge
	}
	now := p.now().UTC()
	p.seq++
	r := Refund{ID: p.seq, PaymentID: id, Amount: Amount{Cents: cents}, Status: StatusApproved, DateCreated: now}
	pay.Refunds = append(pay.Refunds, r)
	pay.TransactionRefund.Cents += cents
	if pay.TransactionRefund.Cents == pay.TransactionAmount.Cents {
		pay.Status, pay.StatusDetail = StatusRefunded, DetailRefunded
	} else {
		pay.StatusDetail = DetailPartiallyRefunded
	}
	pay.DateLastUpdated = now
	p.refundsByKey[key] = r
	p.changed(pay)
	return r, nil
}

func (p *Provider) schedule(d time.Duration, f func()) {
	p.timers = append(p.timers, time.AfterFunc(max(d, 0), f))
}

// changed avisa na ordem das mudanças (o Notifier só enfileira: a entrega é dele, fora daqui).
func (p *Provider) changed(pay *Payment) {
	if p.notify != nil {
		snapshot := *pay
		snapshot.Refunds = append([]Refund(nil), pay.Refunds...)
		p.notify.PaymentChanged(snapshot)
	}
}
