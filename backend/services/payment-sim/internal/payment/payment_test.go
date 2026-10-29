package payment

import (
	"encoding/json"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// recorder guarda os avisos, na ordem.
type recorder struct {
	mu   sync.Mutex
	seen []Payment
}

func (r *recorder) PaymentChanged(p Payment) {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.seen = append(r.seen, p)
}

func (r *recorder) statuses(id int64) []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	var out []string
	for _, p := range r.seen {
		if p.ID == id {
			out = append(out, p.Status)
		}
	}
	return out
}

func newProvider(t *testing.T, opts Options) (*Provider, *recorder) {
	t.Helper()
	rec := &recorder{}
	p := NewProvider(opts, rec)
	t.Cleanup(p.Close)
	return p, rec
}

const visa = "4235647728025682" // passa no Luhn

func card(t *testing.T, p *Provider, holder string) CardToken {
	t.Helper()
	tok, err := p.TokenizeCard(visa, "123", holder, 12, time.Now().Year()+2)
	require.NoError(t, err)
	return tok
}

func pixRequest(cents int64) CreateRequest {
	return CreateRequest{TransactionAmount: Amount{cents}, PaymentMethodID: "pix", ExternalReference: "pedido-1"}
}

func TestPIX_PaidOrExpired(t *testing.T) {
	p, rec := newProvider(t, Options{PIXExpiration: 50 * time.Millisecond})

	paid, err := p.Create("k1", pixRequest(12090))
	require.NoError(t, err)
	assert.Equal(t, StatusPending, paid.Status)
	td := paid.PointOfInteraction.TransactionData
	assert.True(t, strings.HasPrefix(td.QRCode, "000201"), "BR Code começa pelo payload format")
	assert.Contains(t, td.QRCode, "5406120.90")
	assert.NotEmpty(t, td.QRCodeBase64)
	again, err := p.Create("k1", pixRequest(12090))
	require.NoError(t, err)
	assert.Equal(t, paid.ID, again.ID, "a mesma chave não cria outro")

	_, err = p.PayPIX(paid.ID)
	require.NoError(t, err)

	expired, err := p.Create("k2", pixRequest(5000))
	require.NoError(t, err)
	require.Eventually(t, func() bool {
		got, _ := p.Get(expired.ID)
		return got.Status == StatusCancelled && got.StatusDetail == DetailExpired
	}, time.Second, 10*time.Millisecond)
	_, err = p.PayPIX(expired.ID)
	require.ErrorIs(t, err, ErrInvalidState, "PIX expirado não se paga")

	assert.Equal(t, []string{StatusPending, StatusApproved}, rec.statuses(paid.ID))
	assert.Equal(t, []string{StatusPending, StatusCancelled}, rec.statuses(expired.ID))
}

func TestCard_OutcomeByCardholder(t *testing.T) {
	p, _ := newProvider(t, Options{ReviewDelay: 20 * time.Millisecond})
	cases := map[string]string{"APRO": DetailAccredited, "Ana Souza": DetailAccredited, "FUND": DetailInsufficientFunds,
		"SECU": DetailBadSecurityCode, "FRAU": DetailHighRisk, "OTHE": DetailOtherReason}
	for holder, detail := range cases {
		tok := card(t, p, holder)
		got, err := p.Create("k-"+holder, CreateRequest{TransactionAmount: Amount{1000}, PaymentMethodID: "visa", Token: tok.ID, Installments: 3})
		require.NoError(t, err, holder)
		assert.Equal(t, detail, got.StatusDetail, holder)
		assert.Equal(t, 3, got.Installments)
	}

	tok := card(t, p, "CONT") // em análise, depois aprovado
	review, err := p.Create("k-cont", CreateRequest{TransactionAmount: Amount{1000}, PaymentMethodID: "visa", Token: tok.ID})
	require.NoError(t, err)
	assert.Equal(t, StatusInProcess, review.Status)
	require.Eventually(t, func() bool { got, _ := p.Get(review.ID); return got.Status == StatusApproved }, time.Second, 10*time.Millisecond)

	_, err = p.Create("k-reuse", CreateRequest{TransactionAmount: Amount{1000}, PaymentMethodID: "visa", Token: tok.ID})
	require.ErrorIs(t, err, ErrInvalidToken, "token de uso único")
}

func TestCard_AuthorizeCaptureOrCancel(t *testing.T) {
	p, _ := newProvider(t, Options{})
	noCapture := false
	authorize := func(key string) Payment {
		got, err := p.Create(key, CreateRequest{TransactionAmount: Amount{20000}, PaymentMethodID: "visa", Token: card(t, p, "APRO").ID, Capture: &noCapture})
		require.NoError(t, err)
		require.Equal(t, StatusAuthorized, got.Status)
		return got
	}

	a := authorize("a")
	_, err := p.Refund(a.ID, "r0", nil)
	require.ErrorIs(t, err, ErrInvalidState, "autorizado não se estorna: cancela")
	captured, err := p.Capture(a.ID)
	require.NoError(t, err)
	assert.True(t, captured.Captured)
	_, err = p.Capture(a.ID) // repetido
	require.NoError(t, err)

	b := authorize("b")
	voided, err := p.Cancel(b.ID)
	require.NoError(t, err)
	assert.Equal(t, DetailByCollector, voided.StatusDetail)
	_, err = p.Capture(b.ID)
	require.ErrorIs(t, err, ErrInvalidState)
}

func TestRefund_PartialThenTotal_Idempotent(t *testing.T) {
	p, _ := newProvider(t, Options{})
	pay, err := p.Create("k", pixRequest(10000))
	require.NoError(t, err)
	_, err = p.PayPIX(pay.ID)
	require.NoError(t, err)

	part, err := p.Refund(pay.ID, "r1", &Amount{3000})
	require.NoError(t, err)
	same, err := p.Refund(pay.ID, "r1", &Amount{3000})
	require.NoError(t, err)
	assert.Equal(t, part.ID, same.ID)
	got, _ := p.Get(pay.ID)
	assert.Equal(t, (StatusApproved + "/" + DetailPartiallyRefunded), got.Status+"/"+got.StatusDetail)

	_, err = p.Refund(pay.ID, "r2", &Amount{8000})
	require.ErrorIs(t, err, ErrRefundTooLarge)
	rest, err := p.Refund(pay.ID, "r3", nil) // o resto
	require.NoError(t, err)
	assert.Equal(t, int64(7000), rest.Amount.Cents)
	got, _ = p.Get(pay.ID)
	assert.Equal(t, StatusRefunded, got.Status)
	assert.Len(t, got.Refunds, 2)
}

func TestTokenize_Validates(t *testing.T) {
	p, _ := newProvider(t, Options{})
	year := time.Now().Year() + 1
	_, err := p.TokenizeCard("4235647728025683", "123", "APRO", 1, year) // Luhn errado
	require.ErrorIs(t, err, ErrInvalidCard)
	_, err = p.TokenizeCard(visa, "12", "APRO", 1, year)
	require.ErrorIs(t, err, ErrInvalidCard)
	_, err = p.TokenizeCard(visa, "123", "APRO", 1, 2000)
	require.ErrorIs(t, err, ErrInvalidCard)
	tok, err := p.TokenizeCard("5031 4332 1540 6351", "123", "APRO", 11, year)
	require.NoError(t, err)
	assert.Equal(t, ("master" + "6351"), tok.PaymentMethodID+tok.LastFourDigits)
}

func TestAmount_JSON(t *testing.T) {
	var a Amount
	require.NoError(t, json.Unmarshal([]byte("120.9"), &a))
	assert.Equal(t, int64(12090), a.Cents)
	require.NoError(t, json.Unmarshal([]byte("15"), &a))
	assert.Equal(t, int64(1500), a.Cents)
	require.Error(t, json.Unmarshal([]byte("1.234"), &a))
	out, err := json.Marshal(Amount{12090})
	require.NoError(t, err)
	assert.Equal(t, "120.90", string(out))
}

func TestCRC16_MatchesTheBRCodeReference(t *testing.T) {
	// valor de referência do CRC-16/CCITT-FALSE para "123456789"
	assert.Equal(t, uint16(0x29B1), crc16("123456789"))
}

// O simulador guarda tudo em memória: reiniciado, não pode repetir o id de um pagamento que a loja já
// gravou (o payments tem índice único no id do provedor).
func TestIDs_DoNotRepeatAfterARestart(t *testing.T) {
	first, _ := newProvider(t, Options{})
	before, err := first.Create("k1", pixRequest(1000))
	require.NoError(t, err)
	time.Sleep(2 * time.Millisecond)
	restarted, _ := newProvider(t, Options{})
	after, err := restarted.Create("k1", pixRequest(1000))
	require.NoError(t, err)
	assert.Greater(t, after.ID, before.ID)
}
