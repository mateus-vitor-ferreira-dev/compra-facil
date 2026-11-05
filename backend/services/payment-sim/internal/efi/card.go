package efi

import (
	"fmt"
	"net/mail"
	"regexp"
	"strings"
	"time"
)

// Status de cobrança da API Cobranças.
const (
	StatusNew       = "new"
	StatusWaiting   = "waiting"
	StatusApproved  = "approved"
	StatusPaid      = "paid"
	StatusUnpaid    = "unpaid"
	StatusRefunded  = "refunded"
	StatusCanceled  = "canceled"
	StatusContested = "contested"
)

type Item struct {
	Name   string `json:"name"`
	Value  int64  `json:"value"`
	Amount int64  `json:"amount"`
}

type Shipping struct {
	Name      string `json:"name"`
	Value     int64  `json:"value"`
	PayeeCode string `json:"payee_code,omitempty"`
}

type Metadata struct {
	CustomID        *string `json:"custom_id"`
	NotificationURL *string `json:"notification_url"`
}

type Address struct {
	Street       string  `json:"street"`
	Number       string  `json:"number"`
	Neighborhood string  `json:"neighborhood"`
	Zipcode      string  `json:"zipcode"`
	City         string  `json:"city"`
	Complement   *string `json:"complement"`
	State        string  `json:"state"`
}

type Customer struct {
	Name        string `json:"name"`
	Cpf         string `json:"cpf"`
	Email       string `json:"email"`
	PhoneNumber string `json:"phone_number"`
	Birth       string `json:"birth,omitempty"`
}

type CreditCard struct {
	Customer       *Customer `json:"customer"`
	Installments   int       `json:"installments"`
	PaymentToken   string    `json:"payment_token"`
	BillingAddress *Address  `json:"billing_address"`
}

// PaymentRequest é o corpo do POST /v1/charge/:id/pay (e a parte payment do one-step).
type PaymentRequest struct {
	Payment struct {
		CreditCard *CreditCard `json:"credit_card"`
	} `json:"payment"`
}

// ChargeRequest é o corpo do POST /v1/charge (e a parte da cobrança do one-step).
type ChargeRequest struct {
	Items     []Item     `json:"items"`
	Shippings []Shipping `json:"shippings"`
	Metadata  *Metadata  `json:"metadata"`
}

// OneStepRequest é o corpo do POST /v1/charge/one-step.
type OneStepRequest struct {
	ChargeRequest
	PaymentRequest
}

// Event é uma mudança de status, como o GET /v1/notification/:token devolve.
type Event struct {
	CreatedAt   string             `json:"created_at"`
	CustomID    *string            `json:"custom_id"`
	ID          int                `json:"id"`
	Identifiers map[string]int64   `json:"identifiers"`
	Status      map[string]*string `json:"status"`
	Type        string             `json:"type"`
	Value       *int64             `json:"value,omitempty"`
	ReceivedAt  *string            `json:"received_by_bank_at,omitempty"`
}

type History struct {
	Message   string `json:"message"`
	CreatedAt string `json:"created_at"`
}

// Charge é uma cobrança de cartão.
type Charge struct {
	ID               int64
	Status           string
	Total            int64
	CustomID         *string
	NotificationURL  *string
	CreatedAt        time.Time
	Items            []Item
	Shippings        []Shipping
	Customer         *Customer
	Installments     int
	InstallmentValue int64
	Mask             string
	Billing          *Address
	Reason           string
	Retry            bool
	Refunded         int64
	History          []History

	token           string
	events          []Event
	refunding       bool
	lastPartialDate string
}

// cardToken é o cartão tokenizado no navegador: a loja só vê o payment_token.
type cardToken struct {
	last4, mask, brand string
	reuse, used        bool
}

var (
	reName  = regexp.MustCompile(`^[ ]*([A-Za-zÀ-ÿ]+[ ]+)+[A-Za-zÀ-ÿ]+[ ]*$`)
	rePhone = regexp.MustCompile(`^[1-9]{2}9?[0-9]{8}$`)
	reToken = regexp.MustCompile(`^[a-fA-F0-9]{40}$`)
	brands  = map[string]bool{"visa": true, "mastercard": true, "amex": true, "elo": true, "hipercard": true}
)

// CardData é o que a biblioteca payment-token-efi recebe em setCreditCardData.
type CardData struct {
	Brand           string `json:"brand"`
	Number          string `json:"number"`
	CVV             string `json:"cvv"`
	ExpirationMonth string `json:"expiration_month"`
	ExpirationYear  string `json:"expiration_year"`
	HolderName      string `json:"holder_name"`
	HolderDocument  string `json:"holder_document"`
	Reuse           bool   `json:"reuse"`
}

// Tokenize valida o cartão como a biblioteca da Efí (e o Luhn) e devolve o payment_token e a máscara.
func (p *Provider) Tokenize(payee string, c CardData) (token, mask string, err error) {
	if payee != p.opts.PayeeCode {
		return "", "", chargeErr(401, 3500008, "invalid_request", "Requisição não autorizada. Identificador de conta inválido.")
	}
	number := strings.ReplaceAll(c.Number, " ", "")
	now := p.now().In(brasilia)
	year, month := 0, 0
	_, _ = fmt.Sscanf(c.ExpirationYear, "%d", &year)
	_, _ = fmt.Sscanf(c.ExpirationMonth, "%d", &month)
	switch {
	case !brands[c.Brand]:
		return "", "", chargeErr(400, 3500056, "invalid_brand", "O parâmetro [brand] informado é inválido.")
	case len(number) < 13 || len(number) > 19 || !digitsOnly(number) || !luhn(number):
		return "", "", chargeErr(400, 3500057, "invalid_card_number", "O número do cartão é inválido.")
	case len(c.CVV) < 3 || len(c.CVV) > 4 || !digitsOnly(c.CVV):
		return "", "", chargeErr(400, 3500011, "invalid_data", "Cvv deve conter entre 3 e 4 caracteres")
	case len(c.ExpirationMonth) != 2 || month < 1 || month > 12:
		return "", "", chargeErr(400, 3500060, "invalid_expiration_month", "O mês de vencimento é inválido.")
	case len(c.ExpirationYear) != 4 || year < now.Year() || year > now.Year()+11 || (year == now.Year() && month < int(now.Month())):
		return "", "", chargeErr(400, 3500061, "invalid_expiration_year", "O cartão está vencido ou o ano é inválido.")
	case c.HolderName != "" && (len(c.HolderName) < 3 || len(c.HolderName) > 50 || len(strings.Fields(c.HolderName)) < 2):
		return "", "", chargeErr(400, 3500011, "invalid_data", "O nome do titular precisa de nome e sobrenome.")
	case c.HolderDocument != "" && (!digitsOnly(c.HolderDocument) || (len(c.HolderDocument) != 11 && len(c.HolderDocument) != 14)):
		return "", "", chargeErr(400, 3500011, "invalid_data", "O documento do titular é inválido.")
	}
	p.mu.Lock()
	defer p.mu.Unlock()
	token = randomHex(20)
	mask = strings.Repeat("X", len(number)-4) + number[len(number)-4:]
	p.cards[token] = &cardToken{last4: number[len(number)-4:], mask: number[:6] + strings.Repeat("X", len(number)-10) + number[len(number)-4:],
		brand: c.Brand, reuse: c.Reuse}
	return token, mask, nil
}

// CreateCharge é o primeiro passo: a cobrança sem forma de pagamento (status new). Nada é cobrado.
func (p *Provider) CreateCharge(req ChargeRequest) (Charge, error) {
	if len(req.Items) == 0 {
		return Charge{}, chargeErr(400, 3500023, "validation_error", "A propriedade [items] é obrigatória.")
	}
	total := int64(0)
	for _, it := range req.Items {
		amount := max(it.Amount, 1)
		if strings.TrimSpace(it.Name) == "" || len(it.Name) > 255 || it.Value <= 0 {
			return Charge{}, chargeErr(400, 3500034, "validation_error", "Item com nome ou valor inválido.")
		}
		total += it.Value * amount
	}
	for _, s := range req.Shippings {
		if s.Value < 0 {
			return Charge{}, chargeErr(400, 3500034, "validation_error", "Frete com valor inválido.")
		}
		total += s.Value
	}
	p.mu.Lock()
	defer p.mu.Unlock()
	now := p.now()
	p.seq++
	c := &Charge{ID: p.seq, Status: StatusNew, Total: total, CreatedAt: now, Items: req.Items, Shippings: req.Shippings,
		token: newUUID()}
	if req.Metadata != nil {
		c.CustomID, c.NotificationURL = req.Metadata.CustomID, req.Metadata.NotificationURL
	}
	p.charges[c.ID] = c
	p.byToken[c.token] = c.ID
	p.history(c, "Cobrança criada")
	p.setStatus(c, StatusNew, nil)
	return p.copyOf(c), nil
}

// Pay é o segundo passo: o cartão pelo payment_token. Recusa não é erro: a cobrança fica unpaid com o motivo.
func (p *Provider) Pay(id int64, req PaymentRequest) (Charge, error) {
	card := req.Payment.CreditCard
	if card == nil {
		return Charge{}, chargeErr(400, 3500023, "validation_error", "A propriedade [credit_card] é obrigatória.")
	}
	if err := validateCustomer(card.Customer); err != nil {
		return Charge{}, err
	}
	installments := max(card.Installments, 1)
	if installments > 12 {
		return Charge{}, chargeErr(400, 4600089, "invalid_data", "O número de parcelas excede o máximo permitido.")
	}
	if !reToken.MatchString(card.PaymentToken) {
		return Charge{}, chargeErr(400, 3500034, "validation_error", "A propriedade [payment_token] é inválida.")
	}
	p.mu.Lock()
	defer p.mu.Unlock()
	c, ok := p.charges[id]
	if !ok {
		return Charge{}, chargeErr(404, 4600001, "charge_not_found", "Cobrança não encontrada.")
	}
	if c.Status != StatusNew {
		return Charge{}, chargeErr(400, 3500030, "validation_error", "A transação já possui forma de pagamento.")
	}
	tok, ok := p.cards[card.PaymentToken]
	if !ok || (tok.used && !tok.reuse) {
		return Charge{}, chargeErr(400, 3500034, "validation_error", "O payment_token informado é inválido ou já foi usado.")
	}
	tok.used = true
	c.Customer, c.Installments, c.Billing, c.Mask = card.Customer, installments, card.BillingAddress, tok.mask
	c.InstallmentValue = (c.Total + int64(installments) - 1) / int64(installments)
	p.history(c, "Pagamento via cartão de crédito aguardando confirmação")
	p.setStatus(c, StatusWaiting, nil)
	// homologação da Efí: o último dígito do cartão decide
	switch tok.last4[3] {
	case '1':
		p.refuse(c, "Dados do cartão inválidos.", false)
	case '2':
		p.refuse(c, "Transação não autorizada por motivos de segurança.", false)
	case '3':
		p.refuse(c, "Transação não autorizada, tente novamente mais tarde.", true)
	default:
		p.history(c, "Pagamento aprovado pela operadora do cartão")
		p.setStatus(c, StatusApproved, nil)
		cid := c.ID
		p.schedule(p.opts.SettleDelay, func() { _, _ = p.Settle(cid) })
	}
	return p.copyOf(c), nil
}

// OneStep cria e paga numa chamada só.
func (p *Provider) OneStep(req OneStepRequest) (Charge, error) {
	if err := validateCustomer(nilSafeCustomer(req.Payment.CreditCard)); err != nil {
		return Charge{}, err
	}
	c, err := p.CreateCharge(req.ChargeRequest)
	if err != nil {
		return Charge{}, err
	}
	return p.Pay(c.ID, req.PaymentRequest)
}

func nilSafeCustomer(c *CreditCard) *Customer {
	if c == nil {
		return nil
	}
	return c.Customer
}

func validateCustomer(c *Customer) error {
	switch {
	case c == nil:
		return chargeErr(400, 3500023, "validation_error", "A propriedade [customer] é obrigatória.")
	case !reName.MatchString(c.Name):
		return chargeErr(400, 3500034, "validation_error", "A propriedade [customer.name] precisa de nome e sobrenome, só com letras.")
	case len(c.Cpf) != 11 || !digitsOnly(c.Cpf):
		return chargeErr(400, 3500034, "validation_error", "A propriedade [customer.cpf] é inválida.")
	case func() bool { _, err := mail.ParseAddress(c.Email); return err != nil }():
		return chargeErr(400, 3500034, "validation_error", "A propriedade [customer.email] é inválida.")
	case !rePhone.MatchString(c.PhoneNumber):
		return chargeErr(400, 3500034, "validation_error", "A propriedade [customer.phone_number] é inválida.")
	}
	return nil
}

func (p *Provider) refuse(c *Charge, reason string, retry bool) {
	c.Reason, c.Retry = reason, retry
	p.history(c, "Falha no pagamento - "+reason)
	p.setStatus(c, StatusUnpaid, nil)
}

// Settle liquida o cartão aprovado (approved → paid). Só daqui em diante ele pode ser estornado.
func (p *Provider) Settle(id int64) (Charge, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	c, ok := p.charges[id]
	if !ok {
		return Charge{}, chargeErr(404, 4600001, "charge_not_found", "Cobrança não encontrada.")
	}
	if c.Status != StatusApproved {
		return p.copyOf(c), nil
	}
	p.history(c, "Pagamento confirmado")
	value := c.Total
	p.setStatus(c, StatusPaid, &value)
	return p.copyOf(c), nil
}

// Contest abre um chargeback na cobrança paga (rota /sim, para testar a disputa).
func (p *Provider) Contest(id int64) (Charge, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	c, ok := p.charges[id]
	if !ok {
		return Charge{}, chargeErr(404, 4600001, "charge_not_found", "Cobrança não encontrada.")
	}
	if c.Status != StatusPaid {
		return Charge{}, chargeErr(400, 3500081, "validation_error", "A cobrança não possui status [paid].")
	}
	p.history(c, "Pagamento contestado pelo portador do cartão")
	p.setStatus(c, StatusContested, nil)
	return p.copyOf(c), nil
}

// Cancel cancela a cobrança: só em new, waiting ou unpaid. A aprovada ou paga se desfaz pelo estorno.
func (p *Provider) Cancel(id int64) error {
	p.mu.Lock()
	defer p.mu.Unlock()
	c, ok := p.charges[id]
	if !ok {
		return chargeErr(404, 4600001, "charge_not_found", "Cobrança não encontrada.")
	}
	if c.Status != StatusNew && c.Status != StatusWaiting && c.Status != StatusUnpaid {
		return chargeErr(400, 3500043, "validation_error", "Apenas transações com status [new],[link],[waiting] ou [unpaid] podem ser canceladas")
	}
	p.history(c, "Cobrança cancelada")
	p.setStatus(c, StatusCanceled, nil)
	return nil
}

// Refund pede o estorno (amount nil: o total que falta). Só da cobrança paga, um de cada vez, e o parcial no
// máximo um por dia, como na Efí. O estorno é processado depois de RefundDelay; o total vira refunded.
func (p *Provider) Refund(id int64, amount *int64) error {
	p.mu.Lock()
	defer p.mu.Unlock()
	c, ok := p.charges[id]
	if !ok {
		return chargeErr(404, 4600001, "charge_not_found", "Cobrança não encontrada.")
	}
	if c.Status != StatusPaid {
		return chargeErr(400, 3500034, "validation_error", "Só cobranças com status [paid] podem ser estornadas.")
	}
	if c.refunding {
		return chargeErr(400, 3500034, "validation_error", "Já existe um estorno em processamento para esta cobrança.")
	}
	left := c.Total - c.Refunded
	cents := left
	if amount != nil {
		cents = *amount
	}
	if cents <= 0 || cents > left {
		return chargeErr(400, 3500034, "validation_error", "O valor do estorno é maior que o disponível.")
	}
	today := p.now().In(brasilia).Format(time.DateOnly)
	partial := cents < left
	if partial && c.lastPartialDate == today {
		return chargeErr(400, 3500034, "validation_error", "Só é permitido um estorno parcial por dia para a mesma cobrança.")
	}
	if partial {
		c.lastPartialDate = today
	}
	c.refunding = true
	p.schedule(p.opts.RefundDelay, func() { p.finishRefund(id, cents) })
	return nil
}

func (p *Provider) finishRefund(id, cents int64) {
	p.mu.Lock()
	defer p.mu.Unlock()
	c, ok := p.charges[id]
	if !ok || !c.refunding {
		return
	}
	c.refunding = false
	c.Refunded += cents
	p.history(c, fmt.Sprintf("Estorno de R$ %s realizado", strings.Replace(string(ReaisOf(cents)), ".", ",", 1)))
	if c.Refunded == c.Total {
		p.setStatus(c, StatusRefunded, nil)
	}
}

// Charge devolve a cobrança (GET /v1/charge/:id).
func (p *Provider) Charge(id int64) (Charge, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	c, ok := p.charges[id]
	if !ok {
		return Charge{}, chargeErr(404, 4600001, "charge_not_found", "Cobrança não encontrada.")
	}
	return p.copyOf(c), nil
}

// Charges lista todas (rota /sim).
func (p *Provider) Charges() []Charge {
	p.mu.Lock()
	defer p.mu.Unlock()
	out := make([]Charge, 0, len(p.charges))
	for _, c := range p.charges {
		out = append(out, p.copyOf(c))
	}
	return out
}

// Notifications devolve todas as mudanças do token (GET /v1/notification/:token).
func (p *Provider) Notifications(token string) ([]Event, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	id, ok := p.byToken[token]
	if !ok {
		return nil, chargeErr(400, 3500034, "validation_error", "Token de notificação inválido.")
	}
	return append([]Event(nil), p.charges[id].events...), nil
}

func (p *Provider) history(c *Charge, message string) {
	c.History = append(c.History, History{Message: message, CreatedAt: efiTime(p.now())})
}

func (p *Provider) setStatus(c *Charge, status string, value *int64) {
	var previous *string
	if len(c.events) > 0 {
		prev := c.Status
		previous = &prev
	}
	c.Status = status
	current := status
	e := Event{CreatedAt: efiTime(p.now()), CustomID: c.CustomID, ID: len(c.events) + 1,
		Identifiers: map[string]int64{"charge_id": c.ID}, Status: map[string]*string{"current": &current, "previous": previous},
		Type: "charge", Value: value}
	if status == StatusPaid {
		day := p.now().In(brasilia).Format(time.DateOnly)
		e.ReceivedAt = &day
	}
	c.events = append(c.events, e)
	if p.notify != nil && c.NotificationURL != nil && *c.NotificationURL != "" {
		p.notify.ChargeChanged(*c.NotificationURL, c.token)
	}
}

func (p *Provider) copyOf(c *Charge) Charge {
	out := *c
	out.History = append([]History(nil), c.History...)
	out.events = nil
	return out
}

// NotificationToken é o token de notificação da cobrança (testes e /sim).
func (p *Provider) NotificationToken(id int64) string {
	p.mu.Lock()
	defer p.mu.Unlock()
	if c, ok := p.charges[id]; ok {
		return c.token
	}
	return ""
}

func luhn(digits string) bool {
	sum := 0
	for i := range digits {
		d := int(digits[len(digits)-1-i] - '0')
		if i%2 == 1 {
			d *= 2
			if d > 9 {
				d -= 9
			}
		}
		sum += d
	}
	return sum%10 == 0
}

func newUUID() string {
	h := randomHex(16)
	return h[0:8] + "-" + h[8:12] + "-4" + h[13:16] + "-a" + h[17:20] + "-" + h[20:32]
}
