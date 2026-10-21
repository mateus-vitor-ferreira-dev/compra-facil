// Package auth valida e emite os JWT dos operadores (HS256 com segredo compartilhado).
//
// Não há serviço de identidade no projeto: o segredo fica na configuração e o comando
// cmd/token emite tokens para uso local e nos testes de carga.
package auth

import (
	"errors"
	"fmt"
	"slices"
	"time"

	"github.com/golang-jwt/jwt/v5"
)

const RoleAdmin = "admin"

// MinSecretLen evita segredos fracos: HS256 pede pelo menos 256 bits.
const MinSecretLen = 32

var ErrInvalidToken = errors.New("token inválido")

// Claims são os campos do token que o catálogo usa.
type Claims struct {
	Roles []string `json:"roles"`
	jwt.RegisteredClaims
}

func (c Claims) HasRole(role string) bool { return slices.Contains(c.Roles, role) }

type Options struct {
	Secret   string
	Issuer   string
	Audience string
	// Leeway tolera diferença de relógio entre quem emite e quem valida.
	Leeway time.Duration
}

type Verifier struct {
	opts   Options
	parser *jwt.Parser
}

func NewVerifier(opts Options) (*Verifier, error) {
	if len(opts.Secret) < MinSecretLen {
		return nil, fmt.Errorf("segredo do JWT deve ter pelo menos %d caracteres", MinSecretLen)
	}
	parser := jwt.NewParser(
		jwt.WithValidMethods([]string{jwt.SigningMethodHS256.Alg()}), // barra alg=none e troca de algoritmo
		jwt.WithIssuer(opts.Issuer),
		jwt.WithAudience(opts.Audience),
		jwt.WithExpirationRequired(),
		jwt.WithLeeway(opts.Leeway),
	)
	return &Verifier{opts: opts, parser: parser}, nil
}

// Verify confere assinatura, emissor, audiência e validade. O motivo da recusa fica no erro
// (para o log); o cliente recebe só "token inválido".
func (v *Verifier) Verify(raw string) (Claims, error) {
	var claims Claims
	_, err := v.parser.ParseWithClaims(raw, &claims, func(*jwt.Token) (any, error) {
		return []byte(v.opts.Secret), nil
	})
	if err != nil {
		return Claims{}, fmt.Errorf("%w: %w", ErrInvalidToken, err)
	}
	if claims.Subject == "" {
		return Claims{}, fmt.Errorf("%w: sem sub", ErrInvalidToken)
	}
	return claims, nil
}

// Issue emite um token para subject com as roles dadas, válido por ttl.
func Issue(opts Options, subject string, roles []string, ttl time.Duration, now time.Time) (string, error) {
	claims := Claims{
		Roles: roles,
		RegisteredClaims: jwt.RegisteredClaims{
			Subject:   subject,
			Issuer:    opts.Issuer,
			Audience:  jwt.ClaimStrings{opts.Audience},
			IssuedAt:  jwt.NewNumericDate(now),
			NotBefore: jwt.NewNumericDate(now),
			ExpiresAt: jwt.NewNumericDate(now.Add(ttl)),
		},
	}
	signed, err := jwt.NewWithClaims(jwt.SigningMethodHS256, claims).SignedString([]byte(opts.Secret))
	if err != nil {
		return "", fmt.Errorf("assinando token: %w", err)
	}
	return signed, nil
}
