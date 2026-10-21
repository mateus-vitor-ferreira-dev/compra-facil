package handler

import (
	"log/slog"
	"net/http"
	"strings"

	"github.com/gin-gonic/gin"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/auth"
)

const ctxSubject = "subject"

type tokenVerifier interface {
	Verify(raw string) (auth.Claims, error)
}

// RequireRole exige um Bearer JWT válido (401) que tenha a role pedida (403).
func RequireRole(v tokenVerifier, role string, log *slog.Logger) gin.HandlerFunc {
	return func(c *gin.Context) {
		raw, ok := bearerToken(c.GetHeader("Authorization"))
		if !ok {
			unauthorized(c, "envie Authorization: Bearer <token>")
			return
		}
		claims, err := v.Verify(raw)
		if err != nil {
			log.WarnContext(c.Request.Context(), "token recusado",
				slog.String("error", err.Error()),
				slog.String(ctxRequestID, c.GetString(ctxRequestID)),
			)
			unauthorized(c, "token inválido ou expirado")
			return
		}
		if !claims.HasRole(role) {
			WriteProblem(c, http.StatusForbidden, "Acesso negado", "é preciso a role "+role)
			return
		}
		c.Set(ctxSubject, claims.Subject)
		c.Next()
	}
}

func bearerToken(header string) (string, bool) {
	scheme, token, ok := strings.Cut(header, " ")
	token = strings.TrimSpace(token)
	return token, ok && strings.EqualFold(scheme, "Bearer") && token != ""
}

func unauthorized(c *gin.Context, detail string) {
	c.Header("WWW-Authenticate", `Bearer realm="catalog"`)
	WriteProblem(c, http.StatusUnauthorized, "Não autenticado", detail)
}
