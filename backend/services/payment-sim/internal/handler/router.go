// Package handler expõe a API no formato do Mercado Pago (o subconjunto que a loja usa) e as rotas /sim
// de demonstração.
package handler

import (
	"crypto/subtle"
	"errors"
	"html/template"
	"log/slog"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/gin-gonic/gin"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/payment-sim/internal/payment"
)

type Credentials struct {
	// AccessToken vai em Authorization: Bearer nas rotas /v1/payments (o servidor da loja)
	AccessToken string
	// PublicKey vai em ?public_key= no /v1/card_tokens (no Mercado Pago, quem chama é o navegador)
	PublicKey string
}

type API struct {
	provider *payment.Provider
	creds    Credentials
	log      *slog.Logger
}

func NewRouter(provider *payment.Provider, creds Credentials, log *slog.Logger) *gin.Engine {
	gin.SetMode(gin.ReleaseMode)
	a := &API{provider: provider, creds: creds, log: log}
	r := gin.New()
	r.Use(gin.Recovery(), a.accessLog)
	r.GET("/health", func(c *gin.Context) { c.JSON(http.StatusOK, gin.H{"status": "ok"}) })

	v1 := r.Group("/v1")
	v1.POST("/card_tokens", a.requirePublicKey, a.tokenize)
	pays := v1.Group("/payments", a.requireAccessToken)
	pays.POST("", a.create)
	pays.GET("/:id", a.get)
	pays.PUT("/:id", a.update)
	pays.POST("/:id/refunds", a.refund)
	pays.GET("/:id/refunds", a.refunds)

	// demonstração e testes, sem token: o banco do cliente pagando o PIX, e o estado de tudo
	sim := r.Group("/sim")
	sim.GET("/payments", func(c *gin.Context) { c.JSON(http.StatusOK, a.provider.List()) })
	sim.POST("/payments/:id/pay", a.payPIX)
	sim.GET("/pix/:id", a.pixPage)
	sim.POST("/pix/:id", a.pixPagePay)
	sim.POST("/reset", func(c *gin.Context) { a.provider.Reset(); c.Status(http.StatusNoContent) })
	return r
}

func (a *API) accessLog(c *gin.Context) {
	start := time.Now()
	c.Next()
	if c.FullPath() != "/health" {
		a.log.Info("requisição", "method", c.Request.Method, "path", c.Request.URL.Path,
			"status", c.Writer.Status(), "duration_ms", time.Since(start).Milliseconds())
	}
}

// apiError segue o corpo de erro do Mercado Pago.
func apiError(c *gin.Context, status int, code, message string) {
	c.AbortWithStatusJSON(status, gin.H{
		"message": message, "error": code, "status": status,
		"cause": []gin.H{{"code": code, "description": message}},
	})
}

func same(a, b string) bool { return subtle.ConstantTimeCompare([]byte(a), []byte(b)) == 1 }

func (a *API) requireAccessToken(c *gin.Context) {
	token, ok := strings.CutPrefix(c.GetHeader("Authorization"), "Bearer ")
	if !ok || !same(token, a.creds.AccessToken) {
		apiError(c, http.StatusUnauthorized, "unauthorized", "invalid access token")
	}
}

func (a *API) requirePublicKey(c *gin.Context) {
	if !same(c.Query("public_key"), a.creds.PublicKey) {
		apiError(c, http.StatusUnauthorized, "unauthorized", "invalid public key")
	}
}

type tokenRequest struct {
	CardNumber      string `json:"card_number"`
	SecurityCode    string `json:"security_code"`
	ExpirationMonth int    `json:"expiration_month"`
	ExpirationYear  int    `json:"expiration_year"`
	Cardholder      struct {
		Name string `json:"name"`
	} `json:"cardholder"`
}

func (a *API) tokenize(c *gin.Context) {
	var req tokenRequest
	if c.ShouldBindJSON(&req) != nil {
		apiError(c, http.StatusBadRequest, "bad_request", "invalid body")
		return
	}
	t, err := a.provider.TokenizeCard(req.CardNumber, req.SecurityCode, req.Cardholder.Name, req.ExpirationMonth, req.ExpirationYear)
	if err != nil {
		apiError(c, http.StatusBadRequest, "invalid_card", err.Error())
		return
	}
	c.JSON(http.StatusCreated, t)
}

func (a *API) create(c *gin.Context) {
	var req payment.CreateRequest
	if c.ShouldBindJSON(&req) != nil {
		apiError(c, http.StatusBadRequest, "bad_request", "invalid body")
		return
	}
	p, err := a.provider.Create(c.GetHeader("X-Idempotency-Key"), req)
	if err != nil {
		a.fail(c, err)
		return
	}
	c.JSON(http.StatusCreated, p)
}

func (a *API) get(c *gin.Context) {
	id, ok := pathID(c)
	if !ok {
		return
	}
	p, err := a.provider.Get(id)
	if err != nil {
		a.fail(c, err)
		return
	}
	c.JSON(http.StatusOK, p)
}

// update é o PUT do Mercado Pago: {"capture": true} captura; {"status": "cancelled"} cancela.
func (a *API) update(c *gin.Context) {
	id, ok := pathID(c)
	if !ok {
		return
	}
	var req struct {
		Capture *bool  `json:"capture"`
		Status  string `json:"status"`
	}
	if c.ShouldBindJSON(&req) != nil {
		apiError(c, http.StatusBadRequest, "bad_request", "invalid body")
		return
	}
	var (
		p   payment.Payment
		err error
	)
	switch {
	case req.Capture != nil && *req.Capture:
		p, err = a.provider.Capture(id)
	case req.Status == payment.StatusCancelled:
		p, err = a.provider.Cancel(id)
	default:
		apiError(c, http.StatusBadRequest, "bad_request", "use capture=true or status=cancelled")
		return
	}
	if err != nil {
		a.fail(c, err)
		return
	}
	c.JSON(http.StatusOK, p)
}

func (a *API) refund(c *gin.Context) {
	id, ok := pathID(c)
	if !ok {
		return
	}
	var req struct {
		Amount *payment.Amount `json:"amount"`
	}
	// corpo vazio é estorno total
	if c.Request.ContentLength != 0 && c.ShouldBindJSON(&req) != nil {
		apiError(c, http.StatusBadRequest, "bad_request", "invalid body")
		return
	}
	r, err := a.provider.Refund(id, c.GetHeader("X-Idempotency-Key"), req.Amount)
	if err != nil {
		a.fail(c, err)
		return
	}
	c.JSON(http.StatusCreated, r)
}

func (a *API) refunds(c *gin.Context) {
	id, ok := pathID(c)
	if !ok {
		return
	}
	p, err := a.provider.Get(id)
	if err != nil {
		a.fail(c, err)
		return
	}
	c.JSON(http.StatusOK, p.Refunds)
}

func (a *API) payPIX(c *gin.Context) {
	id, ok := pathID(c)
	if !ok {
		return
	}
	p, err := a.provider.PayPIX(id)
	if err != nil {
		a.fail(c, err)
		return
	}
	a.log.Info("PIX pago pelo banco do cliente", "payment_id", id)
	c.JSON(http.StatusOK, p)
}

func (a *API) fail(c *gin.Context, err error) {
	switch {
	case errors.Is(err, payment.ErrNotFound):
		apiError(c, http.StatusNotFound, "not_found", "payment not found")
	case errors.Is(err, payment.ErrInvalidToken):
		apiError(c, http.StatusBadRequest, "invalid_token", err.Error())
	case errors.Is(err, payment.ErrInvalidRequest), errors.Is(err, payment.ErrRefundTooLarge):
		apiError(c, http.StatusBadRequest, "bad_request", err.Error())
	case errors.Is(err, payment.ErrInvalidState):
		apiError(c, http.StatusConflict, "invalid_status", err.Error())
	default:
		a.log.Error("erro inesperado", "error", err.Error())
		apiError(c, http.StatusInternalServerError, "internal_error", "internal error")
	}
}

func pathID(c *gin.Context) (int64, bool) {
	id, err := strconv.ParseInt(c.Param("id"), 10, 64)
	if err != nil {
		apiError(c, http.StatusNotFound, "not_found", "payment not found")
		return 0, false
	}
	return id, true
}

// ---------- a página "pague o PIX" (o ticket_url): faz o papel do app do banco ----------

var pixTemplate = template.Must(template.New("pix").Parse(`<!doctype html>
<html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>PIX simulado</title>
<style>body{font-family:system-ui,sans-serif;max-width:28rem;margin:2rem auto;padding:0 1rem;color:#1b1f3b}
img{width:14rem;height:14rem}code{display:block;word-break:break-all;background:#eef;padding:.5rem;border-radius:.25rem;font-size:.8rem}
button{font:inherit;font-weight:600;min-height:2.75rem;padding:0 1.25rem;border-radius:.375rem;border:0;background:#1b1f3b;color:#fff;cursor:pointer}
.status{font-weight:600}</style></head><body>
<h1>PIX simulado</h1>
<p>Este é o banco de mentira do ambiente de teste: nenhum dinheiro de verdade se move.</p>
<p class="status">{{.Amount}} · {{.Status}}</p>
{{if .QR}}<img alt="QR code do PIX" src="data:image/png;base64,{{.QR}}">{{end}}
<p>Copia e cola:</p><code>{{.Code}}</code>
{{if .Payable}}<form method="post"><p><button type="submit">Pagar o PIX</button></p></form>{{end}}
</body></html>`))

func (a *API) pixPage(c *gin.Context) {
	id, ok := pathID(c)
	if !ok {
		return
	}
	p, err := a.provider.Get(id)
	if err != nil || p.PointOfInteraction == nil || p.PointOfInteraction.TransactionData == nil {
		c.String(http.StatusNotFound, "PIX não encontrado")
		return
	}
	td := p.PointOfInteraction.TransactionData
	status := map[string]string{payment.StatusPending: "aguardando o pagamento", payment.StatusApproved: "pago",
		payment.StatusCancelled: "expirado", payment.StatusRefunded: "estornado"}[p.Status]
	c.Header("Content-Type", "text/html; charset=utf-8")
	_ = pixTemplate.Execute(c.Writer, map[string]any{
		"Amount": "R$ " + strings.Replace(string(must(p.TransactionAmount.MarshalJSON())), ".", ",", 1),
		"Status": status, "QR": td.QRCodeBase64, "Code": td.QRCode, "Payable": p.Status == payment.StatusPending,
	})
}

func (a *API) pixPagePay(c *gin.Context) {
	id, ok := pathID(c)
	if !ok {
		return
	}
	if _, err := a.provider.PayPIX(id); err != nil && !errors.Is(err, payment.ErrInvalidState) {
		a.fail(c, err)
		return
	}
	c.Redirect(http.StatusSeeOther, c.Request.URL.Path)
}

func must(b []byte, _ error) []byte { return b }
