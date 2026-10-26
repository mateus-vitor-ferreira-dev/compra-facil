package auth_test

import (
	"context"
	"encoding/json"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"sync/atomic"
	"testing"
	"time"

	"github.com/golang-jwt/jwt/v5"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/auth"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/auth/authtest"
)

func TestVerify(t *testing.T) {
	identity := authtest.New(t)
	v := identity.Verifier(t)

	t.Run("token do identity", func(t *testing.T) {
		claims, err := v.Verify(identity.Token(t, []string{auth.RoleAdmin}))

		require.NoError(t, err)
		assert.Equal(t, "0199f6a0-0000-7000-8000-000000000a1a", claims.Subject)
		assert.Equal(t, "ana@exemplo.com", claims.Email)
		assert.True(t, claims.HasRole(auth.RoleAdmin))
		assert.False(t, claims.HasRole(auth.RoleCustomer))
	})

	other := authtest.New(t) // outra chave com o mesmo formato: um identity falso
	recusados := map[string]func() string{
		"expirado": func() string {
			return identity.Token(t, nil, func(c *auth.Claims) {
				c.ExpiresAt = jwt.NewNumericDate(time.Now().Add(-time.Minute))
			})
		},
		"assinado por outra chave": func() string { return other.Token(t, []string{auth.RoleAdmin}) },
		"outro emissor": func() string {
			return identity.Token(t, nil, func(c *auth.Claims) { c.Issuer = "terceiro" })
		},
		"outra audiência": func() string {
			return identity.Token(t, nil, func(c *auth.Claims) { c.Audience = jwt.ClaimStrings{"outra-loja"} })
		},
		"sem sub":  func() string { return identity.Token(t, nil, func(c *auth.Claims) { c.Subject = "" }) },
		"sem exp":  func() string { return identity.Token(t, nil, func(c *auth.Claims) { c.ExpiresAt = nil }) },
		"lixo":     func() string { return "nao.e.jwt" },
		"alg none": func() string { return unsigned(t, jwt.SigningMethodNone, jwt.UnsafeAllowNoneSignatureType) },
		// o antigo JWT de dev (HS256 com segredo compartilhado) não vale mais
		"HS256": func() string {
			return unsigned(t, jwt.SigningMethodHS256, []byte("dev-only-compra-facil-jwt-secret-troque-em-producao"))
		},
	}
	for name, token := range recusados {
		t.Run(name, func(t *testing.T) {
			_, err := v.Verify(token())
			assert.ErrorIs(t, err, auth.ErrInvalidToken)
		})
	}
}

func unsigned(t *testing.T, method jwt.SigningMethod, key any) string {
	t.Helper()
	tok, err := jwt.NewWithClaims(method, auth.Claims{Roles: []string{auth.RoleAdmin}, RegisteredClaims: jwt.RegisteredClaims{
		Subject: "ana", Issuer: authtest.Options.Issuer, Audience: jwt.ClaimStrings{authtest.Options.Audience},
		ExpiresAt: jwt.NewNumericDate(time.Now().Add(time.Hour)),
	}}).SignedString(key)
	require.NoError(t, err)
	return tok
}

// O caminho de produção: o JWKS por HTTP, e a chave nova da rotação aceita sem reiniciar.
func TestNewVerifier_BuscaOJwks_EAprendeAChaveNova(t *testing.T) {
	first, next := authtest.New(t), authtest.New(t)
	var current atomic.Pointer[authtest.Identity]
	current.Store(first)
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/.well-known/jwks.json" {
			http.NotFound(w, r)
			return
		}
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write(current.Load().JWKS(t))
	}))
	t.Cleanup(srv.Close)
	opts := authtest.Options
	opts.Authority = srv.URL + "/"

	ctx, cancel := context.WithCancel(context.Background())
	t.Cleanup(cancel)
	v, err := auth.NewVerifier(ctx, opts, slog.New(slog.DiscardHandler))
	require.NoError(t, err)

	require.Eventually(t, func() bool {
		_, err := v.Verify(first.Token(t, []string{auth.RoleAdmin}))
		return err == nil
	}, 5*time.Second, 50*time.Millisecond)

	// o identity rotacionou: o kid desconhecido faz o verifier buscar o JWKS de novo
	current.Store(next)
	_, err = v.Verify(next.Token(t, []string{auth.RoleAdmin}))
	require.NoError(t, err)
}

func TestNewVerifier_SemAuthority(t *testing.T) {
	_, err := auth.NewVerifier(context.Background(), auth.Options{}, slog.Default())
	assert.Error(t, err)
}

// garante que o JWKS de teste tem o formato do contrato do identity (o mesmo que o keyfunc lê)
func TestAuthtest_JwksComOsCamposDoContrato(t *testing.T) {
	var jwks struct {
		Keys []map[string]string `json:"keys"`
	}
	require.NoError(t, json.Unmarshal(authtest.New(t).JWKS(t), &jwks))
	require.Len(t, jwks.Keys, 1)
	for _, field := range []string{"kty", "use", "alg", "kid", "n", "e"} {
		assert.NotEmpty(t, jwks.Keys[0][field], field)
	}
}
