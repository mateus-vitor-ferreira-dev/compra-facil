package handler

import "github.com/gin-gonic/gin"

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
