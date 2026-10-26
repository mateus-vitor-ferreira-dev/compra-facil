// Package authtest faz o papel do identity nos testes: um par de chaves RSA, o JWKS dele e tokens
// assinados com o formato dos de verdade (RS256, kid, iss, aud, sub, roles, exp).
package authtest

import (
	"crypto/rand"
	"crypto/rsa"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"math/big"
	"testing"
	"time"

	"github.com/golang-jwt/jwt/v5"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/auth"
)

// Options do identity local: os mesmos emissor e audiência da configuração padrão.
var Options = auth.Options{Issuer: "compra-facil-identity", Audience: "compra-facil", Authority: "http://identity.test"}

type Identity struct {
	key *rsa.PrivateKey
	kid string
}

func New(t testing.TB) *Identity {
	t.Helper()
	i, err := newIdentity()
	if err != nil {
		t.Fatal(err)
	}
	return i
}

// MustNew é o New para variável de pacote (um identity para todos os testes do pacote).
func MustNew() *Identity {
	i, err := newIdentity()
	if err != nil {
		panic(err)
	}
	return i
}

func newIdentity() (*Identity, error) {
	key, err := rsa.GenerateKey(rand.Reader, 2048)
	if err != nil {
		return nil, fmt.Errorf("gerando chave RSA: %w", err)
	}
	return &Identity{key: key, kid: "teste-" + base64.RawURLEncoding.EncodeToString(key.N.Bytes()[:6])}, nil
}

// JWKS é o que o identity publica em /.well-known/jwks.json.
func (i *Identity) JWKS(t testing.TB) json.RawMessage {
	t.Helper()
	raw, err := json.Marshal(map[string]any{"keys": []map[string]string{{
		"kty": "RSA", "use": "sig", "alg": "RS256", "kid": i.kid,
		"n": base64.RawURLEncoding.EncodeToString(i.key.N.Bytes()),
		"e": base64.RawURLEncoding.EncodeToString(big.NewInt(int64(i.key.E)).Bytes()),
	}}})
	if err != nil {
		t.Fatal(err)
	}
	return raw
}

// Verifier valida com o JWKS desta chave, como o catálogo faz com o do identity.
func (i *Identity) Verifier(t testing.TB) *auth.Verifier {
	t.Helper()
	v, err := auth.NewStaticVerifier(Options, i.JWKS(t))
	if err != nil {
		t.Fatal(err)
	}
	return v
}

// Token assina um access token; edit muda as claims antes (outro emissor, já vencido...).
func (i *Identity) Token(t testing.TB, roles []string, edit ...func(*auth.Claims)) string {
	t.Helper()
	now := time.Now()
	claims := auth.Claims{
		Roles: roles,
		Email: "ana@exemplo.com",
		RegisteredClaims: jwt.RegisteredClaims{
			Subject:   "0199f6a0-0000-7000-8000-000000000a1a",
			Issuer:    Options.Issuer,
			Audience:  jwt.ClaimStrings{Options.Audience},
			IssuedAt:  jwt.NewNumericDate(now),
			NotBefore: jwt.NewNumericDate(now),
			ExpiresAt: jwt.NewNumericDate(now.Add(15 * time.Minute)),
		},
	}
	for _, e := range edit {
		e(&claims)
	}
	tok := jwt.NewWithClaims(jwt.SigningMethodRS256, claims)
	tok.Header["kid"] = i.kid
	signed, err := tok.SignedString(i.key)
	if err != nil {
		t.Fatal(err)
	}
	return signed
}
