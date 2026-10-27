package cache

import (
	"context"
	"net/http"
)

// Header informa na resposta de onde veio a leitura: HIT (Redis), MISS (banco, gravado no cache)
// ou BYPASS (Redis indisponível, direto no banco). O cenário k6 calcula o hit ratio por ele.
const Header = "X-Cache"

var headerValues = map[string]string{"hit": "HIT", "miss": "MISS", "error": "BYPASS"}

type headerKey struct{}

// HeaderMiddleware deixa os headers da resposta no contexto para cached marcar o X-Cache.
// Funciona porque a leitura acontece antes do handler escrever o corpo.
func HeaderMiddleware(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		next.ServeHTTP(w, r.WithContext(context.WithValue(r.Context(), headerKey{}, w.Header())))
	})
}

func setHeader(ctx context.Context, result string) {
	if h, ok := ctx.Value(headerKey{}).(http.Header); ok {
		h.Set(Header, headerValues[result])
	}
}
