// Package efi é o provedor de pagamento simulado no contrato da Efí (#379): a API Cobranças para o cartão
// (cobrança em dois passos ou one-step, liquidação, estorno, notificações por token) e a API Pix para o PIX
// (cobrança imediata, QR, devolução e webhook). É dublê de teste: estado só em memória, comportamento
// determinístico, e as regras de homologação da Efí (o final do cartão decide; devolução de R$ 0,01 é recusada).
package efi

import (
	"crypto/rand"
	"encoding/hex"
	"errors"
	"fmt"
	"strconv"
	"strings"
	"sync"
	"time"
)

// Error é um erro da API, no formato de cada uma: Cobranças usa code/error/error_description; Pix, nome/mensagem.
type Error struct {
	HTTP    int
	Code    int    // Cobranças: o code numérico (3500034...)
	Name    string // Cobranças: error; Pix: nome
	Message string
}

func (e *Error) Error() string { return fmt.Sprintf("%s: %s", e.Name, e.Message) }

func chargeErr(http, code int, name, message string) *Error {
	return &Error{HTTP: http, Code: code, Name: name, Message: message}
}

func pixErr(http int, name, message string) *Error {
	return &Error{HTTP: http, Name: name, Message: message}
}

// ErrUnauthorized é o 401 em texto puro das duas APIs (credencial ou token inválido).
var ErrUnauthorized = errors.New("unauthorized")

// Notifier entrega os avisos: o POST do token (cartão) e o webhook do PIX. Só enfileira; a entrega é dele.
type Notifier interface {
	// ChargeChanged: POST form notification=<token> no notification_url da cobrança.
	ChargeChanged(url, token string)
	// PixChanged: POST {"pix":[item]} no webhook cadastrado para a chave.
	PixChanged(webhookURL string, item PixItem)
}

type Options struct {
	ClientID, ClientSecret string
	// PayeeCode é o "identificador de conta" que o navegador usa para tokenizar o cartão.
	PayeeCode string
	// PixKey é a chave PIX da conta (a única que o PUT /v2/cob aceita).
	PixKey string
	// SettleDelay: quanto o cartão fica approved antes de virar paid (a liquidação, que só a Efí decide).
	SettleDelay time.Duration
	// RefundDelay: quanto um estorno fica em processamento.
	RefundDelay time.Duration
	// PayPageURL monta a página "pague o PIX" (o linkVisualizacao) a partir do código aleatório da cobrança.
	PayPageURL func(token string) string
}

// Provider guarda tudo e aplica as regras. Seguro para uso concorrente.
type Provider struct {
	opts   Options
	notify Notifier
	now    func() time.Time

	mu       sync.Mutex
	seq      int64
	access   map[string]time.Time // access_token → validade
	cards    map[string]*cardToken
	charges  map[int64]*Charge
	byToken  map[string]int64 // token de notificação → cobrança
	cobs     map[string]*Cob  // txid → cobrança PIX
	locs     map[int64]string // loc.id → txid
	pages    map[string]string
	pixByE2E map[string]string // endToEndId → txid
	webhooks map[string]Webhook
	timers   []*time.Timer
}

func NewProvider(opts Options, notify Notifier) *Provider {
	if opts.SettleDelay <= 0 {
		opts.SettleDelay = 5 * time.Second
	}
	if opts.RefundDelay <= 0 {
		opts.RefundDelay = time.Second
	}
	if opts.PayPageURL == nil {
		opts.PayPageURL = func(token string) string { return "http://localhost:8092/sim/cob/pagar/" + token }
	}
	p := &Provider{opts: opts, notify: notify, now: time.Now}
	p.resetLocked()
	return p
}

func (p *Provider) resetLocked() {
	for _, t := range p.timers {
		t.Stop()
	}
	p.timers = nil
	// os ids partem do horário: reiniciado, o simulador não repete o id de uma cobrança que a loja já tem
	p.seq = p.now().Unix()
	p.access, p.cards, p.charges, p.byToken = map[string]time.Time{}, map[string]*cardToken{}, map[int64]*Charge{}, map[string]int64{}
	p.cobs, p.locs, p.pages, p.pixByE2E = map[string]*Cob{}, map[int64]string{}, map[string]string{}, map[string]string{}
	p.webhooks = map[string]Webhook{}
}

// Reset apaga tudo (rota /sim/reset), menos os webhooks cadastrados: a loja os cadastra uma vez, ao subir.
func (p *Provider) Reset() {
	p.mu.Lock()
	defer p.mu.Unlock()
	hooks := p.webhooks
	p.resetLocked()
	p.webhooks = hooks
}

// Close para os timers (desligamento e testes).
func (p *Provider) Close() {
	p.mu.Lock()
	defer p.mu.Unlock()
	for _, t := range p.timers {
		t.Stop()
	}
}

// PayeeCode é o identificador de conta (a rota de tokenização confere).
func (p *Provider) PayeeCode() string { return p.opts.PayeeCode }

// Authorize troca client_id e client_secret por um access_token (POST /v1/authorize e /oauth/token).
func (p *Provider) Authorize(clientID, secret string, ttl time.Duration) (string, error) {
	if clientID != p.opts.ClientID || secret != p.opts.ClientSecret {
		return "", ErrUnauthorized
	}
	p.mu.Lock()
	defer p.mu.Unlock()
	token := randomHex(32)
	p.access[token] = p.now().Add(ttl)
	return token, nil
}

// Authenticated confere o Bearer.
func (p *Provider) Authenticated(token string) bool {
	p.mu.Lock()
	defer p.mu.Unlock()
	expires, ok := p.access[token]
	return ok && p.now().Before(expires)
}

func (p *Provider) schedule(d time.Duration, f func()) {
	p.timers = append(p.timers, time.AfterFunc(max(d, 0), f))
}

func randomHex(n int) string {
	b := make([]byte, n)
	_, _ = rand.Read(b)
	return hex.EncodeToString(b)
}

const alnum = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"

func randomAlnum(n int) string {
	b := make([]byte, n)
	_, _ = rand.Read(b)
	for i := range b {
		b[i] = alnum[int(b[i])%len(alnum)]
	}
	return string(b)
}

func isAlnum(s string, minLen, maxLen int) bool {
	if len(s) < minLen || len(s) > maxLen {
		return false
	}
	for _, r := range s {
		if !strings.ContainsRune(alnum, r) {
			return false
		}
	}
	return true
}

func digitsOnly(s string) bool { return s != "" && strings.Trim(s, "0123456789") == "" }

// Reais é um valor da API Pix: string com ponto e duas casas ("123.45").
type Reais string

// ReaisOf formata centavos como a API Pix.
func ReaisOf(cents int64) Reais { return Reais(fmt.Sprintf("%d.%02d", cents/100, cents%100)) }

// Cents lê o valor; fora do formato \d{1,10}\.\d{2}, erro.
func (r Reais) Cents() (int64, bool) {
	whole, frac, ok := strings.Cut(string(r), ".")
	if !ok || len(frac) != 2 || len(whole) < 1 || len(whole) > 10 || !digitsOnly(whole) || !digitsOnly(frac) {
		return 0, false
	}
	w, _ := strconv.ParseInt(whole, 10, 64)
	f, _ := strconv.ParseInt(frac, 10, 64)
	return w*100 + f, true
}

// efiTime é o formato das datas da API Cobranças ("2022-08-16 11:33:52", horário de Brasília).
func efiTime(t time.Time) string { return t.In(brasilia).Format("2006-01-02 15:04:05") }

var brasilia = time.FixedZone("BRT", -3*60*60)
