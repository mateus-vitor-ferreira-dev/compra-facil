package handler

import (
	"net/http"
	"strconv"

	"github.com/gin-gonic/gin"
)

// Problem segue a RFC 9457 (ProblemDetails), o formato de erro combinado entre os serviços.
type Problem struct {
	Type      string `json:"type"`
	Title     string `json:"title"`
	Status    int    `json:"status"`
	Detail    string `json:"detail,omitempty"`
	Instance  string `json:"instance,omitempty"`
	RequestID string `json:"request_id,omitempty"`
}

func WriteProblem(c *gin.Context, status int, title, detail string) {
	c.Header("Content-Type", "application/problem+json")
	c.AbortWithStatusJSON(status, Problem{
		Type:      "about:blank",
		Title:     title,
		Status:    status,
		Detail:    detail,
		Instance:  c.Request.URL.Path,
		RequestID: c.GetString(ctxRequestID),
	})
}

// gatewayTitles são os erros que o gateway (Traefik) gera sozinho, sem corpo no formato do contrato.
var gatewayTitles = map[int]string{
	http.StatusTooManyRequests:    "Muitas requisições",
	http.StatusBadGateway:         "Serviço indisponível",
	http.StatusServiceUnavailable: "Serviço indisponível",
	http.StatusGatewayTimeout:     "Serviço não respondeu a tempo",
}

// GatewayProblem serve o corpo ProblemDetails dos erros do gateway: o middleware "errors" do Traefik
// pede GET /problems/{status} e devolve este corpo ao cliente com o status original (ex.: 429 do
// rate limit). Status fora da lista é 404, para a rota não virar um gerador de erros arbitrários.
func GatewayProblem(c *gin.Context) {
	status, err := strconv.Atoi(c.Param("status"))
	title, ok := gatewayTitles[status]
	if err != nil || !ok {
		WriteProblem(c, http.StatusNotFound, "Recurso não encontrado", "")
		return
	}
	detail := ""
	if status == http.StatusTooManyRequests {
		detail = "Limite de requisições do catálogo excedido; tente de novo em instantes."
		// o Traefik descarta os headers do rate limit ao trocar o corpo: o Retry-After vem daqui
		// (o limite é por segundo)
		c.Header("Retry-After", "1")
	}
	// sem instance: o Traefik não repassa a URL original, e /problems/429 enganaria quem lê
	c.Header("Content-Type", "application/problem+json")
	c.AbortWithStatusJSON(status, Problem{
		Type: "about:blank", Title: title, Status: status, Detail: detail, RequestID: c.GetString(ctxRequestID),
	})
}
