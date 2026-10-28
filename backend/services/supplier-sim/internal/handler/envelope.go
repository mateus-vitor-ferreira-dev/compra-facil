// Package handler expõe a API no formato da CJ Dropshipping v2 (subconjunto usado pela loja).
package handler

import (
	"net/http"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
)

// Códigos de resposta. 1600001 e 1600100 seguem a CJ; os 1603xxx são do simulador
// (a tabela real da CJ não foi conferida para esses casos).
const (
	CodeSuccess       = 200
	CodeAuth          = 1600001
	CodeInvalidParams = 1600100
	CodeOutOfStock    = 1603001
	CodeRejected      = 1603002
	CodeShipped       = 1603003
	CodeNotFound      = 1603404
)

// Envelope é o corpo de toda resposta da CJ.
type Envelope struct {
	Code      int    `json:"code"`
	Result    bool   `json:"result"`
	Message   string `json:"message"`
	Data      any    `json:"data"`
	RequestID string `json:"requestId"`
}

func ok(c *gin.Context, data any) {
	c.JSON(http.StatusOK, Envelope{Code: CodeSuccess, Result: true, Message: "Success", Data: data, RequestID: uuid.NewString()})
}

// fail responde erro no envelope. Erros de negócio vão com HTTP 200, como a CJ faz;
// só autenticação usa 401.
func fail(c *gin.Context, httpStatus, code int, msg string) {
	c.AbortWithStatusJSON(httpStatus, Envelope{Code: code, Result: false, Message: msg, RequestID: uuid.NewString()})
}
