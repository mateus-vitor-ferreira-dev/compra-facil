package handler

import (
	"errors"
	"html/template"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/gin-gonic/gin"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/payment-sim/internal/efi"
)

// As APIs da Efí (#379): Cobranças (cartão) e Pix, cada uma com o seu token; a tokenização do cartão que o
// navegador faz (no lugar da biblioteca payment-token-efi, que só fala com os hosts da Efí); e a página que faz o
// papel do app do banco no PIX.
func (a *API) registerEfi(r *gin.Engine) {
	r.POST("/v1/authorize", a.efiAuthorize(600*time.Second, false))
	r.POST("/oauth/token", a.efiAuthorize(3600*time.Second, true))

	charges := r.Group("/v1", a.requireEfiBearer)
	charges.POST("/charge", a.efiCreateCharge)
	charges.POST("/charge/one-step", a.efiOneStep)
	charges.POST("/charge/:id/pay", a.efiPay)
	charges.GET("/charge/:id", a.efiGetCharge)
	charges.PUT("/charge/:id/cancel", a.efiCancel)
	charges.POST("/charge/card/:id/refund", a.efiRefund)
	charges.GET("/notification/:token", a.efiNotification)

	pix := r.Group("/v2", a.requireEfiBearer)
	pix.PUT("/cob/:txid", a.efiCreateCob)
	pix.GET("/cob/:txid", a.efiGetCob)
	pix.PATCH("/cob/:txid", a.efiPatchCob)
	pix.GET("/loc/:id/qrcode", a.efiQRCode)
	pix.PUT("/pix/:e2e/devolucao/:id", a.efiRequestRefund)
	pix.GET("/pix/:e2e/devolucao/:id", a.efiGetRefund)
	pix.PUT("/webhook/:chave", a.efiSetWebhook)
	pix.GET("/webhook/:chave", a.efiGetWebhook)

	r.OPTIONS("/sim/card-token", cardTokenCORS)
	r.POST("/sim/card-token", cardTokenCORS, a.efiTokenize)
	r.GET("/sim/cob/pagar/:token", a.efiPayPage)
	r.POST("/sim/cob/pagar/:token", a.efiPayPagePay)

	sim := r.Group("/sim/efi", a.requireAdminToken)
	sim.GET("/charges", func(c *gin.Context) {
		out := []gin.H{}
		for _, ch := range a.efi.Charges() {
			out = append(out, chargeView(ch))
		}
		c.JSON(http.StatusOK, out)
	})
	sim.POST("/charges/:id/settle", a.efiSimCharge(a.efi.Settle))
	sim.POST("/charges/:id/contest", a.efiSimCharge(a.efi.Contest))
	sim.GET("/cobs", func(c *gin.Context) {
		out := []gin.H{}
		for _, cob := range a.efi.Cobs() {
			out = append(out, cobView(cob, true))
		}
		c.JSON(http.StatusOK, out)
	})
	sim.POST("/cobs/:txid/pay", func(c *gin.Context) {
		cob, err := a.efi.PayCob(c.Param("txid"))
		if err != nil {
			efiFail(c, err)
			return
		}
		c.JSON(http.StatusOK, cobView(cob, true))
	})
}

// efiAuthorize: Basic client_id:client_secret e {"grant_type": "client_credentials"}. Credencial errada é o 401
// em texto puro da Efí.
func (a *API) efiAuthorize(ttl time.Duration, pix bool) gin.HandlerFunc {
	return func(c *gin.Context) {
		id, secret, ok := c.Request.BasicAuth()
		var body struct {
			GrantType string `json:"grant_type"`
		}
		if !ok || c.ShouldBindJSON(&body) != nil || body.GrantType != "client_credentials" {
			c.String(http.StatusUnauthorized, "unauthorized")
			return
		}
		token, err := a.efi.Authorize(id, secret, ttl)
		if err != nil {
			c.String(http.StatusUnauthorized, "unauthorized")
			return
		}
		if pix {
			c.JSON(http.StatusOK, gin.H{"access_token": token, "token_type": "Bearer", "expires_in": int(ttl.Seconds()),
				"scope": "cob.read cob.write pix.read pix.write webhook.read webhook.write payloadlocation.read"})
			return
		}
		c.JSON(http.StatusOK, gin.H{"access_token": token, "refresh_token": token, "expires_in": int(ttl.Seconds()),
			"expire_at": strconv.FormatInt(time.Now().Add(ttl).UnixMilli(), 10), "token_type": "Bearer"})
	}
}

func (a *API) requireEfiBearer(c *gin.Context) {
	token, ok := strings.CutPrefix(c.GetHeader("Authorization"), "Bearer ")
	if !ok || !a.efi.Authenticated(token) {
		c.String(http.StatusUnauthorized, "Unauthorized")
		c.Abort()
	}
}

// efiFail escreve o erro no formato de cada API.
func efiFail(c *gin.Context, err error) {
	var e *efi.Error
	if !errors.As(err, &e) {
		c.AbortWithStatusJSON(http.StatusInternalServerError, gin.H{"code": 3500000, "error": "internal_error", "error_description": "Erro interno do servidor."})
		return
	}
	if e.Code != 0 {
		c.AbortWithStatusJSON(e.HTTP, gin.H{"code": e.Code, "error": e.Name, "error_description": e.Message})
		return
	}
	c.AbortWithStatusJSON(e.HTTP, gin.H{"nome": e.Name, "mensagem": e.Message})
}

func badJSON(c *gin.Context) {
	c.AbortWithStatusJSON(http.StatusBadRequest, gin.H{"code": 3500034, "error": "validation_error", "error_description": "Corpo da requisição inválido."})
}

func ok(c *gin.Context, data any) { c.JSON(http.StatusOK, gin.H{"code": 200, "data": data}) }

func chargeID(c *gin.Context) (int64, bool) {
	id, err := strconv.ParseInt(c.Param("id"), 10, 64)
	if err != nil {
		c.AbortWithStatusJSON(http.StatusNotFound, gin.H{"code": 4600001, "error": "charge_not_found", "error_description": "Cobrança não encontrada."})
		return 0, false
	}
	return id, true
}

// payView é a resposta do pay e do one-step.
func payView(ch efi.Charge) gin.H {
	v := gin.H{"installments": ch.Installments, "installment_value": ch.InstallmentValue, "charge_id": ch.ID,
		"status": ch.Status, "total": ch.Total, "payment": "credit_card"}
	if ch.Status == efi.StatusUnpaid {
		v["refusal"] = gin.H{"reason": ch.Reason, "retry": ch.Retry}
	}
	return v
}

// chargeView é o GET /v1/charge/:id.
func chargeView(ch efi.Charge) gin.H {
	v := gin.H{"charge_id": ch.ID, "total": ch.Total, "status": ch.Status, "custom_id": ch.CustomID,
		"created_at":       ch.CreatedAt.In(time.FixedZone("BRT", -3*60*60)).Format("2006-01-02 15:04:05"),
		"notification_url": ch.NotificationURL, "items": ch.Items, "history": ch.History, "shippings": ch.Shippings}
	if ch.Shippings == nil {
		v["shippings"] = []efi.Shipping{}
	}
	if ch.Reason != "" {
		v["reason"] = ch.Reason
	}
	if ch.Customer != nil {
		v["customer"] = ch.Customer
		v["payment"] = gin.H{"method": "credit_card", "created_at": v["created_at"], "message": nil,
			"credit_card": gin.H{"mask": ch.Mask, "installments": ch.Installments, "installment_value": ch.InstallmentValue,
				"address": ch.Billing}}
	}
	return v
}

func (a *API) efiCreateCharge(c *gin.Context) {
	var req efi.ChargeRequest
	if c.ShouldBindJSON(&req) != nil {
		badJSON(c)
		return
	}
	ch, err := a.efi.CreateCharge(req)
	if err != nil {
		efiFail(c, err)
		return
	}
	ok(c, gin.H{"charge_id": ch.ID, "status": ch.Status, "total": ch.Total, "custom_id": ch.CustomID,
		"created_at": chargeView(ch)["created_at"]})
}

func (a *API) efiPay(c *gin.Context) {
	id, found := chargeID(c)
	if !found {
		return
	}
	var req efi.PaymentRequest
	if c.ShouldBindJSON(&req) != nil {
		badJSON(c)
		return
	}
	ch, err := a.efi.Pay(id, req)
	if err != nil {
		efiFail(c, err)
		return
	}
	ok(c, payView(ch))
}

func (a *API) efiOneStep(c *gin.Context) {
	var req efi.OneStepRequest
	if c.ShouldBindJSON(&req) != nil {
		badJSON(c)
		return
	}
	ch, err := a.efi.OneStep(req)
	if err != nil {
		efiFail(c, err)
		return
	}
	ok(c, payView(ch))
}

func (a *API) efiGetCharge(c *gin.Context) {
	id, found := chargeID(c)
	if !found {
		return
	}
	ch, err := a.efi.Charge(id)
	if err != nil {
		efiFail(c, err)
		return
	}
	ok(c, chargeView(ch))
}

func (a *API) efiCancel(c *gin.Context) {
	id, found := chargeID(c)
	if !found {
		return
	}
	if err := a.efi.Cancel(id); err != nil {
		efiFail(c, err)
		return
	}
	c.JSON(http.StatusOK, gin.H{"code": 200})
}

func (a *API) efiRefund(c *gin.Context) {
	id, found := chargeID(c)
	if !found {
		return
	}
	var req struct {
		Amount *int64 `json:"amount"`
	}
	if c.Request.ContentLength != 0 && c.ShouldBindJSON(&req) != nil {
		badJSON(c)
		return
	}
	if err := a.efi.Refund(id, req.Amount); err != nil {
		efiFail(c, err)
		return
	}
	ok(c, "Reembolso em processamento")
}

func (a *API) efiNotification(c *gin.Context) {
	events, err := a.efi.Notifications(c.Param("token"))
	if err != nil {
		efiFail(c, err)
		return
	}
	ok(c, events)
}

func (a *API) efiSimCharge(f func(int64) (efi.Charge, error)) gin.HandlerFunc {
	return func(c *gin.Context) {
		id, found := chargeID(c)
		if !found {
			return
		}
		ch, err := f(id)
		if err != nil {
			efiFail(c, err)
			return
		}
		c.JSON(http.StatusOK, chargeView(ch))
	}
}

// cobView é a cobrança PIX. Como nos exemplos da Efí, a validade vem como número na criação e como texto na
// consulta (o adaptador da loja lê os dois).
func cobView(cob efi.Cob, read bool) gin.H {
	var expiracao any = cob.Expiracao
	if read {
		expiracao = strconv.Itoa(cob.Expiracao)
	}
	v := gin.H{"calendario": gin.H{"criacao": cob.Criacao.UTC().Format("2006-01-02T15:04:05.000Z"), "expiracao": expiracao},
		"txid": cob.Txid, "revisao": cob.Revisao, "location": cob.Loc.Location, "status": cob.Status,
		"valor": gin.H{"original": cob.Valor}, "chave": cob.Chave, "pixCopiaECola": cob.PixCopiaECola}
	if !read {
		v["loc"] = cob.Loc
	}
	if cob.Devedor != nil {
		v["devedor"] = cob.Devedor
	}
	if cob.SolicitacaoPagador != "" {
		v["solicitacaoPagador"] = cob.SolicitacaoPagador
	}
	if len(cob.Pix) > 0 {
		v["pix"] = cob.Pix
	}
	return v
}

func pixBadJSON(c *gin.Context) {
	c.AbortWithStatusJSON(http.StatusBadRequest, gin.H{"nome": "json_invalido", "mensagem": "Valores ou tipos de campo inválidos"})
}

func (a *API) efiCreateCob(c *gin.Context) {
	var req efi.CobRequest
	if c.ShouldBindJSON(&req) != nil {
		pixBadJSON(c)
		return
	}
	cob, err := a.efi.CreateCob(c.Param("txid"), req)
	if err != nil {
		efiFail(c, err)
		return
	}
	c.JSON(http.StatusCreated, cobView(cob, false))
}

func (a *API) efiGetCob(c *gin.Context) {
	cob, err := a.efi.Cob(c.Param("txid"))
	if err != nil {
		efiFail(c, err)
		return
	}
	c.JSON(http.StatusOK, cobView(cob, true))
}

func (a *API) efiPatchCob(c *gin.Context) {
	var req struct {
		Status string `json:"status"`
	}
	if c.ShouldBindJSON(&req) != nil || req.Status != efi.CobRemovida {
		pixBadJSON(c)
		return
	}
	cob, err := a.efi.RemoveCob(c.Param("txid"))
	if err != nil {
		efiFail(c, err)
		return
	}
	c.JSON(http.StatusOK, cobView(cob, false))
}

func (a *API) efiQRCode(c *gin.Context) {
	id, err := strconv.ParseInt(c.Param("id"), 10, 64)
	if err != nil {
		c.AbortWithStatusJSON(http.StatusNotFound, gin.H{"nome": "location_nao_encontrada", "mensagem": "Nenhuma location encontrado para o identificador informado"})
		return
	}
	code, image, link, err := a.efi.QRCode(id)
	if err != nil {
		efiFail(c, err)
		return
	}
	c.JSON(http.StatusOK, gin.H{"qrcode": code, "imagemQrcode": image, "linkVisualizacao": link})
}

func (a *API) efiRequestRefund(c *gin.Context) {
	var req struct {
		Valor efi.Reais `json:"valor"`
	}
	if c.ShouldBindJSON(&req) != nil {
		pixBadJSON(c)
		return
	}
	d, err := a.efi.RequestRefund(c.Param("e2e"), c.Param("id"), req.Valor)
	if err != nil {
		efiFail(c, err)
		return
	}
	c.JSON(http.StatusCreated, d)
}

func (a *API) efiGetRefund(c *gin.Context) {
	d, err := a.efi.Devolucao(c.Param("e2e"), c.Param("id"))
	if err != nil {
		efiFail(c, err)
		return
	}
	c.JSON(http.StatusOK, d)
}

func (a *API) efiSetWebhook(c *gin.Context) {
	var req struct {
		WebhookURL string `json:"webhookUrl"`
	}
	if c.ShouldBindJSON(&req) != nil {
		pixBadJSON(c)
		return
	}
	w, err := a.efi.SetWebhook(c.Param("chave"), req.WebhookURL)
	if err != nil {
		efiFail(c, err)
		return
	}
	a.log.Info("webhook do PIX cadastrado", "chave", w.Chave, "skip_mtls", c.GetHeader("x-skip-mtls-checking"))
	c.JSON(http.StatusCreated, w)
}

func (a *API) efiGetWebhook(c *gin.Context) {
	w, found := a.efi.GetWebhook(c.Param("chave"))
	if !found {
		c.AbortWithStatusJSON(http.StatusNotFound, gin.H{"nome": "webhook_nao_encontrado", "mensagem": "Webhook não encontrado para a chave informada"})
		return
	}
	c.JSON(http.StatusOK, w)
}

// cardTokenCORS libera a tokenização para qualquer origem: a rota só aceita o identificador de conta (público, vai
// no navegador) e devolve um token de uso único, como os hosts de tokenização da Efí.
func cardTokenCORS(c *gin.Context) {
	h := c.Writer.Header()
	h.Set("Access-Control-Allow-Origin", "*")
	h.Set("Access-Control-Allow-Methods", "POST, OPTIONS")
	h.Set("Access-Control-Allow-Headers", "Content-Type")
	h.Set("Access-Control-Max-Age", "600")
	if c.Request.Method == http.MethodOptions {
		c.AbortWithStatus(http.StatusNoContent)
	}
}

// efiTokenize faz o papel da payment-token-efi: os dados do cartão viram {payment_token, card_mask}.
func (a *API) efiTokenize(c *gin.Context) {
	var req struct {
		efi.CardData
		PayeeCode string `json:"payee_code"`
	}
	if c.ShouldBindJSON(&req) != nil {
		badJSON(c)
		return
	}
	token, mask, err := a.efi.Tokenize(req.PayeeCode, req.CardData)
	if err != nil {
		efiFail(c, err)
		return
	}
	ok(c, gin.H{"payment_token": token, "card_mask": mask})
}

// ---------- a página de pagamento do PIX (o linkVisualizacao): faz o papel do app do banco ----------

var cobTemplate = template.Must(template.New("cob").Parse(`<!doctype html>
<html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>PIX simulado</title>
<style>body{font-family:system-ui,sans-serif;max-width:28rem;margin:2rem auto;padding:0 1rem;color:#1b1f3b}
img{width:14rem;height:14rem}code{display:block;word-break:break-all;background:#eef;padding:.5rem;border-radius:.25rem;font-size:.8rem}
button{font:inherit;font-weight:600;min-height:2.75rem;padding:0 1.25rem;border-radius:.375rem;border:0;background:#1b1f3b;color:#fff;cursor:pointer}
.status{font-weight:600}</style></head><body>
<h1>PIX simulado</h1>
<p>Este é o banco de mentira do ambiente de teste: nenhum dinheiro de verdade se move.</p>
<p class="status">R$ {{.Amount}} · {{.Status}}</p>
<img alt="QR code do PIX" src="{{.QR}}">
<p>Copia e cola:</p><code>{{.Code}}</code>
{{if .Payable}}<form method="post"><p><button type="submit">Pagar o PIX</button></p></form>{{end}}
</body></html>`))

func (a *API) efiPayPage(c *gin.Context) {
	txid, found := a.efi.ByPage(c.Param("token"))
	if !found {
		c.String(http.StatusNotFound, "PIX não encontrado")
		return
	}
	cob, err := a.efi.Cob(txid)
	if err != nil {
		c.String(http.StatusNotFound, "PIX não encontrado")
		return
	}
	_, image, _, err := a.efi.QRCode(cob.Loc.ID)
	if err != nil {
		c.String(http.StatusInternalServerError, "QR indisponível")
		return
	}
	expired := cob.Expired(time.Now())
	status := map[string]string{efi.CobAtiva: "aguardando o pagamento", efi.CobConcluida: "pago", efi.CobRemovida: "cancelado"}[cob.Status]
	if cob.Status == efi.CobAtiva && expired {
		status = "vencido"
	}
	c.Header("Content-Type", "text/html; charset=utf-8")
	_ = cobTemplate.Execute(c.Writer, map[string]any{
		"Amount": strings.Replace(string(cob.Valor), ".", ",", 1), "Status": status,
		"QR": template.URL(image), "Code": cob.PixCopiaECola, "Payable": cob.Status == efi.CobAtiva && !expired, //nolint:gosec // data URI gerado aqui
	})
}

func (a *API) efiPayPagePay(c *gin.Context) {
	txid, found := a.efi.ByPage(c.Param("token"))
	if !found {
		c.String(http.StatusNotFound, "PIX não encontrado")
		return
	}
	if _, err := a.efi.PayCob(txid); err != nil {
		a.log.Info("PIX não pago pela página", "txid", txid, "error", err.Error())
	} else {
		a.log.Info("PIX pago pelo banco do cliente", "txid", txid)
	}
	c.Redirect(http.StatusSeeOther, c.Request.URL.Path)
}
