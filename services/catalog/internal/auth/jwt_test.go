package auth

import (
	"testing"
	"time"

	"github.com/golang-jwt/jwt/v5"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

var testOpts = Options{Secret: "segredo-de-teste-com-mais-de-32-caracteres", Issuer: "compra-facil", Audience: "catalog"}

func TestVerify(t *testing.T) {
	v, err := NewVerifier(testOpts)
	require.NoError(t, err)
	now := time.Now()

	t.Run("token válido", func(t *testing.T) {
		tok, err := Issue(testOpts, "ana", []string{RoleAdmin}, time.Hour, now)
		require.NoError(t, err)

		claims, err := v.Verify(tok)

		require.NoError(t, err)
		assert.Equal(t, "ana", claims.Subject)
		assert.True(t, claims.HasRole(RoleAdmin))
		assert.False(t, claims.HasRole("outra"))
	})

	other := testOpts
	other.Secret = "outro-segredo-tambem-com-mais-de-32-chars"
	wrongIss := testOpts
	wrongIss.Issuer = "terceiro"
	wrongAud := testOpts
	wrongAud.Audience = "orders"

	recusados := map[string]func() string{
		"expirado": func() string {
			tok, _ := Issue(testOpts, "ana", []string{RoleAdmin}, time.Minute, now.Add(-time.Hour))
			return tok
		},
		"assinado com outro segredo": func() string { tok, _ := Issue(other, "ana", nil, time.Hour, now); return tok },
		"outro emissor":              func() string { tok, _ := Issue(wrongIss, "ana", nil, time.Hour, now); return tok },
		"outra audiência":            func() string { tok, _ := Issue(wrongAud, "ana", nil, time.Hour, now); return tok },
		"sem sub":                    func() string { tok, _ := Issue(testOpts, "", nil, time.Hour, now); return tok },
		"lixo":                       func() string { return "nao.e.jwt" },
		"alg none": func() string {
			tok, _ := jwt.NewWithClaims(jwt.SigningMethodNone, Claims{RegisteredClaims: jwt.RegisteredClaims{
				Subject: "ana", Issuer: testOpts.Issuer, Audience: jwt.ClaimStrings{testOpts.Audience},
				ExpiresAt: jwt.NewNumericDate(now.Add(time.Hour)),
			}}).SignedString(jwt.UnsafeAllowNoneSignatureType)
			return tok
		},
		"sem exp": func() string {
			tok, _ := jwt.NewWithClaims(jwt.SigningMethodHS256, Claims{RegisteredClaims: jwt.RegisteredClaims{
				Subject: "ana", Issuer: testOpts.Issuer, Audience: jwt.ClaimStrings{testOpts.Audience},
			}}).SignedString([]byte(testOpts.Secret))
			return tok
		},
	}
	for name, token := range recusados {
		t.Run(name, func(t *testing.T) {
			_, err := v.Verify(token())
			assert.ErrorIs(t, err, ErrInvalidToken)
		})
	}
}

func TestNewVerifier_SegredoCurto(t *testing.T) {
	_, err := NewVerifier(Options{Secret: "curto"})
	assert.Error(t, err)
}
