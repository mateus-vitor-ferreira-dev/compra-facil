// Package handler expõe o emissor no formato da API da Nuvem Fiscal (o subconjunto que a loja usa): o
// token OAuth2 (client_credentials), a NF-e (emitir, consultar, cancelar, XML e PDF) e as rotas /sim de
// demonstração.
package handler

import (
	"crypto/rand"
	"crypto/subtle"
	"encoding/hex"
	"errors"
	"log/slog"
	"net/http"
	"strings"
	"sync"
	"time"

	"github.com/gin-gonic/gin"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/invoice-sim/internal/nfe"
)

type Credentials struct {
	ClientID     string
	ClientSecret string
	TokenTTL     time.Duration
}

type API struct {
	issuer *nfe.Issuer
	creds  Credentials
	log    *slog.Logger
	now    func() time.Time

	mu     sync.Mutex
	tokens map[string]time.Time // access token → validade
}

func NewRouter(issuer *nfe.Issuer, creds Credentials, log *slog.Logger) *gin.Engine {
	gin.SetMode(gin.ReleaseMode)
	a := &API{issuer: issuer, creds: creds, log: log, now: time.Now, tokens: map[string]time.Time{}}
	r := gin.New()
	r.Use(gin.Recovery(), a.accessLog)
	r.GET("/health", func(c *gin.Context) { c.JSON(http.StatusOK, gin.H{"status": "ok"}) })
	// na Nuvem Fiscal, o token sai de auth.nuvemfiscal.com.br; aqui, do mesmo endereço
	r.POST("/oauth/token", a.token)

	notas := r.Group("/nfe", a.requireToken)
	notas.POST("", a.emit)
	notas.GET("", a.list)
	notas.GET("/:id", a.get)
	notas.POST("/:id/cancelamento", a.cancel)
	notas.GET("/:id/cancelamento", a.getCancel)
	notas.GET("/:id/xml", a.xml)
	notas.GET("/:id/pdf", a.pdf)

	sim := r.Group("/sim")
	sim.GET("/notas", func(c *gin.Context) { c.JSON(http.StatusOK, a.issuer.List()) })
	sim.POST("/reset", func(c *gin.Context) { a.issuer.Reset(); c.Status(http.StatusNoContent) })
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

// apiError segue o corpo de erro da Nuvem Fiscal.
func apiError(c *gin.Context, status int, code, message string) {
	c.AbortWithStatusJSON(status, gin.H{"error": gin.H{"code": code, "message": message}})
}

func same(a, b string) bool { return subtle.ConstantTimeCompare([]byte(a), []byte(b)) == 1 }

// token é o client_credentials do OAuth2, com as credenciais no corpo (como a Nuvem Fiscal aceita).
func (a *API) token(c *gin.Context) {
	if c.PostForm("grant_type") != "client_credentials" {
		c.AbortWithStatusJSON(http.StatusBadRequest, gin.H{"error": "unsupported_grant_type"})
		return
	}
	if !same(c.PostForm("client_id"), a.creds.ClientID) || !same(c.PostForm("client_secret"), a.creds.ClientSecret) {
		c.AbortWithStatusJSON(http.StatusUnauthorized, gin.H{"error": "invalid_client"})
		return
	}
	b := make([]byte, 24)
	if _, err := rand.Read(b); err != nil {
		apiError(c, http.StatusInternalServerError, "InternalError", "sem entropia")
		return
	}
	token := hex.EncodeToString(b)
	a.mu.Lock()
	a.tokens[token] = a.now().Add(a.creds.TokenTTL)
	a.mu.Unlock()
	c.JSON(http.StatusOK, gin.H{
		"access_token": token, "token_type": "bearer", "expires_in": int(a.creds.TokenTTL.Seconds()),
		"scope": c.DefaultPostForm("scope", "nfe"),
	})
}

func (a *API) requireToken(c *gin.Context) {
	token, ok := strings.CutPrefix(c.GetHeader("Authorization"), "Bearer ")
	a.mu.Lock()
	expires, known := a.tokens[token]
	a.mu.Unlock()
	if !ok || !known || a.now().After(expires) {
		apiError(c, http.StatusUnauthorized, "Unauthorized", "token inválido ou vencido")
	}
}

func (a *API) emit(c *gin.Context) {
	var req nfe.Request
	if c.ShouldBindJSON(&req) != nil {
		apiError(c, http.StatusBadRequest, "ValidationFailed", "corpo inválido")
		return
	}
	inv, err := a.issuer.Emit(req)
	if err != nil {
		a.fail(c, err)
		return
	}
	// autorizada ou rejeitada pela SEFAZ, a emissão foi processada: 200, e o status diz o resultado
	c.JSON(http.StatusOK, inv)
}

func (a *API) list(c *gin.Context) {
	ref := strings.TrimSpace(c.Query("referencia"))
	if ref == "" {
		apiError(c, http.StatusBadRequest, "ValidationFailed", "informe a referencia")
		return
	}
	c.JSON(http.StatusOK, gin.H{"data": a.issuer.FindByReferencia(c.DefaultQuery("ambiente", nfe.EnvSandbox), ref)})
}

func (a *API) get(c *gin.Context) {
	inv, err := a.issuer.Get(c.Param("id"))
	if err != nil {
		a.fail(c, err)
		return
	}
	c.JSON(http.StatusOK, inv)
}

func (a *API) cancel(c *gin.Context) {
	var body struct {
		Justificativa string `json:"justificativa"`
	}
	if c.ShouldBindJSON(&body) != nil {
		apiError(c, http.StatusBadRequest, "ValidationFailed", "corpo inválido")
		return
	}
	evt, err := a.issuer.Cancel(c.Param("id"), body.Justificativa)
	if err != nil {
		a.fail(c, err)
		return
	}
	c.JSON(http.StatusOK, evt)
}

func (a *API) getCancel(c *gin.Context) {
	evt, err := a.issuer.GetCancellation(c.Param("id"))
	if err != nil {
		a.fail(c, err)
		return
	}
	c.JSON(http.StatusOK, evt)
}

func (a *API) xml(c *gin.Context) {
	inv, req, ok := a.document(c)
	if !ok {
		return
	}
	body, err := nfe.XML(inv, req)
	if err != nil {
		apiError(c, http.StatusInternalServerError, "InternalError", err.Error())
		return
	}
	c.Data(http.StatusOK, "application/xml", body)
}

func (a *API) pdf(c *gin.Context) {
	inv, req, ok := a.document(c)
	if !ok {
		return
	}
	c.Header("Content-Disposition", `inline; filename="`+inv.Chave+`.pdf"`)
	c.Data(http.StatusOK, "application/pdf", nfe.PDF(inv, req))
}

// document: só a nota autorizada (ou cancelada depois) tem XML e DANFE; a rejeitada não existe para a SEFAZ.
func (a *API) document(c *gin.Context) (nfe.Invoice, nfe.Request, bool) {
	inv, req, err := a.issuer.Document(c.Param("id"))
	if err != nil {
		a.fail(c, err)
		return inv, req, false
	}
	if inv.Status == nfe.StatusRejected {
		apiError(c, http.StatusNotFound, "NotFound", "nota rejeitada não tem XML nem DANFE")
		return inv, req, false
	}
	return inv, req, true
}

func (a *API) fail(c *gin.Context, err error) {
	switch {
	case errors.Is(err, nfe.ErrNotFound):
		apiError(c, http.StatusNotFound, "NotFound", err.Error())
	case errors.Is(err, nfe.ErrInvalid):
		apiError(c, http.StatusBadRequest, "ValidationFailed", err.Error())
	case errors.Is(err, nfe.ErrNotCancel), errors.Is(err, nfe.ErrCancelWindow):
		apiError(c, http.StatusUnprocessableEntity, "UnprocessableEntity", err.Error())
	default:
		a.log.Error("erro inesperado", "err", err)
		apiError(c, http.StatusInternalServerError, "InternalError", "erro inesperado")
	}
}
