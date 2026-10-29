package handler

import (
	"crypto/rand"
	"crypto/subtle"
	"encoding/hex"
	"net/http"
	"sync"
	"time"

	"github.com/gin-gonic/gin"
)

// TokenHeader é onde a CJ espera o access token.
const TokenHeader = "CJ-Access-Token"

// Tokens emite e valida access tokens opacos, guardados só em memória: reiniciar o simulador
// invalida todos e o adaptador da loja precisa saber pedir outro (como faria com a CJ).
type Tokens struct {
	email, password string
	ttl             time.Duration
	now             func() time.Time

	mu     sync.Mutex
	issued map[string]time.Time // token → expiração
}

func NewTokens(email, password string, ttl time.Duration) *Tokens {
	return &Tokens{email: email, password: password, ttl: ttl, now: time.Now, issued: map[string]time.Time{}}
}

type tokenResponse struct {
	AccessToken           string `json:"accessToken"`
	AccessTokenExpiryDate string `json:"accessTokenExpiryDate"`
	RefreshToken          string `json:"refreshToken"`
}

func (t *Tokens) getAccessToken(c *gin.Context) {
	var body struct {
		Email    string `json:"email"`
		Password string `json:"password"`
	}
	if err := c.ShouldBindJSON(&body); err != nil {
		fail(c, http.StatusOK, CodeInvalidParams, "Invalid params")
		return
	}
	// comparação em tempo constante: hábito barato mesmo num simulador
	emailOK := subtle.ConstantTimeCompare([]byte(body.Email), []byte(t.email)) == 1
	passOK := subtle.ConstantTimeCompare([]byte(body.Password), []byte(t.password)) == 1
	if !emailOK || !passOK {
		fail(c, http.StatusUnauthorized, CodeAuth, "Invalid email or password")
		return
	}
	access, exp := randomToken(), t.now().Add(t.ttl).UTC()
	t.mu.Lock()
	t.issued[access] = exp
	t.mu.Unlock()
	ok(c, tokenResponse{AccessToken: access, AccessTokenExpiryDate: exp.Format(time.RFC3339), RefreshToken: randomToken()})
}

// Require barra quem não manda um token emitido e ainda válido.
func (t *Tokens) Require(c *gin.Context) {
	token := c.GetHeader(TokenHeader)
	t.mu.Lock()
	exp, found := t.issued[token]
	t.mu.Unlock()
	if token == "" || !found || t.now().After(exp) {
		fail(c, http.StatusUnauthorized, CodeAuth, "Invalid token")
		return
	}
	c.Next()
}

func randomToken() string {
	b := make([]byte, 32)
	_, _ = rand.Read(b) // crypto/rand.Read não falha (Go 1.24+)
	return hex.EncodeToString(b)
}
