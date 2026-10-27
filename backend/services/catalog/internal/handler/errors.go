package handler

import (
	"errors"
	"log/slog"
	"net/http"

	"github.com/gin-gonic/gin"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
)

type fieldErrorDTO struct {
	Field   string `json:"field"`
	Message string `json:"message"`
}

type validationProblem struct {
	Problem
	Errors []fieldErrorDTO `json:"errors"`
}

// respondError traduz erros de domínio para ProblemDetails. Erros desconhecidos viram 500 sem
// expor a mensagem interna (ela vai só para o log, com o request_id).
func respondError(c *gin.Context, log *slog.Logger, err error) {
	var ve *domain.ValidationError
	switch {
	case errors.As(err, &ve):
		writeValidationProblem(c, ve.Fields)
	case errors.Is(err, domain.ErrNotFound):
		WriteProblem(c, http.StatusNotFound, "Recurso não encontrado", "")
	case errors.Is(err, domain.ErrVersionConflict):
		WriteProblem(c, http.StatusPreconditionFailed, "Versão desatualizada",
			"o recurso mudou desde a última leitura; leia de novo e reenvie com o novo ETag")
	case errors.Is(err, domain.ErrConflict):
		// mensagens de conflito são montadas pelos services para o cliente
		WriteProblem(c, http.StatusConflict, "Conflito", err.Error())
	default:
		log.ErrorContext(c.Request.Context(), "erro inesperado",
			slog.String("error", err.Error()),
			slog.String(ctxRequestID, c.GetString(ctxRequestID)),
		)
		WriteProblem(c, http.StatusInternalServerError, "Erro interno", "")
	}
}

func writeValidationProblem(c *gin.Context, fields []domain.FieldError) {
	errs := make([]fieldErrorDTO, len(fields))
	for i, f := range fields {
		errs[i] = fieldErrorDTO{Field: f.Field, Message: f.Message}
	}
	c.Header("Content-Type", "application/problem+json")
	c.AbortWithStatusJSON(http.StatusUnprocessableEntity, validationProblem{
		Problem: Problem{
			Type:      "about:blank",
			Title:     "Dados inválidos",
			Status:    http.StatusUnprocessableEntity,
			Instance:  c.Request.URL.Path,
			RequestID: c.GetString(ctxRequestID),
		},
		Errors: errs,
	})
}

// badRequest responde 400 para parâmetro com tipo errado (ex.: limit=abc).
func badRequest(c *gin.Context, detail string) {
	WriteProblem(c, http.StatusBadRequest, "Requisição inválida", detail)
}
