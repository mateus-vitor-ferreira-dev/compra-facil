// Package auth valida os JWT emitidos pelo serviço identity (RS256), com as chaves públicas do JWKS dele.
//
// O catálogo nunca emite token nem tem segredo: busca o JWKS do identity, guarda em memória, renova de
// hora em hora e na hora em que chega um token com kid desconhecido (chave nova da rotação).
package auth

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"slices"
	"strings"
	"time"

	"github.com/MicahParks/keyfunc/v3"
	"github.com/golang-jwt/jwt/v5"
	"golang.org/x/time/rate"
)

const (
	RoleAdmin    = "admin"
	RoleCustomer = "customer"
)

var ErrInvalidToken = errors.New("token inválido")

// Claims são os campos do token que o catálogo usa (contracts/openapi/identity.v1.yaml).
type Claims struct {
	Roles []string `json:"roles"`
	Email string   `json:"email"`
	jwt.RegisteredClaims
}

func (c Claims) HasRole(role string) bool { return slices.Contains(c.Roles, role) }

type Options struct {
	// Authority é o endereço do identity (ex.: http://identity:8006); o JWKS fica em /.well-known/jwks.json.
	Authority string
	Issuer    string
	Audience  string
	// Leeway tolera diferença de relógio entre quem emite e quem valida.
	Leeway time.Duration
}

// JWKSURL é onde o identity publica as chaves públicas.
func (o Options) JWKSURL() string {
	return strings.TrimRight(o.Authority, "/") + "/.well-known/jwks.json"
}

type Verifier struct {
	parser *jwt.Parser
	keys   keyfunc.Keyfunc
}

// NewVerifier busca o JWKS do identity em segundo plano: o catálogo sobe mesmo com o identity fora
// (a vitrine não depende dele), e as rotas admin respondem 401 até as chaves chegarem.
func NewVerifier(ctx context.Context, opts Options, log *slog.Logger) (*Verifier, error) {
	if opts.Authority == "" {
		return nil, errors.New("auth.authority (endereço do identity) não configurado")
	}
	keys, err := keyfunc.NewDefaultOverrideCtx(ctx, []string{opts.JWKSURL()}, keyfunc.Override{
		// a próxima chave aparece no JWKS um dia antes de assinar: 1 h de cache sobra
		RefreshInterval: time.Hour,
		// kid desconhecido (chave criada fora da janela) refaz a busca, no máximo a cada 30 s, e a
		// requisição não espera a vez mais que 2 s
		RefreshUnknownKID: rate.NewLimiter(rate.Every(30*time.Second), 1),
		RateLimitWaitMax:  2 * time.Second,
		HTTPTimeout:       5 * time.Second,
		RefreshErrorHandlerFunc: func(u string) func(context.Context, error) {
			return func(ctx context.Context, err error) {
				log.WarnContext(ctx, "falha ao buscar o JWKS do identity", "url", u, "err", err)
			}
		},
	})
	if err != nil {
		return nil, fmt.Errorf("JWKS de %s: %w", opts.JWKSURL(), err)
	}
	return newVerifier(opts, keys), nil
}

// NewStaticVerifier valida com um JWKS fixo, sem HTTP (testes).
func NewStaticVerifier(opts Options, jwks json.RawMessage) (*Verifier, error) {
	keys, err := keyfunc.NewJWKSetJSON(jwks)
	if err != nil {
		return nil, fmt.Errorf("JWKS: %w", err)
	}
	return newVerifier(opts, keys), nil
}

func newVerifier(opts Options, keys keyfunc.Keyfunc) *Verifier {
	parser := jwt.NewParser(
		jwt.WithValidMethods([]string{jwt.SigningMethodRS256.Alg()}), // barra alg=none e HS256 com a chave pública
		jwt.WithIssuer(opts.Issuer),
		jwt.WithAudience(opts.Audience),
		jwt.WithExpirationRequired(),
		jwt.WithLeeway(opts.Leeway),
	)
	return &Verifier{parser: parser, keys: keys}
}

// Verify confere assinatura, emissor, audiência e validade. O motivo da recusa fica no erro
// (para o log); o cliente recebe só "token inválido".
func (v *Verifier) Verify(raw string) (Claims, error) {
	var claims Claims
	if _, err := v.parser.ParseWithClaims(raw, &claims, v.keys.Keyfunc); err != nil {
		return Claims{}, fmt.Errorf("%w: %w", ErrInvalidToken, err)
	}
	if claims.Subject == "" {
		return Claims{}, fmt.Errorf("%w: sem sub", ErrInvalidToken)
	}
	return claims, nil
}
