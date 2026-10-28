package handler

import (
	"errors"
	"log/slog"
	"net/http"
	"strconv"
	"time"

	"github.com/gin-gonic/gin"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/catalog"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/order"
)

const maxPageSize = 200

type API struct {
	tokens *Tokens
	stock  *catalog.Stock
	orders *order.Simulator
	log    *slog.Logger
}

func NewRouter(tokens *Tokens, stock *catalog.Stock, orders *order.Simulator, log *slog.Logger) *gin.Engine {
	gin.SetMode(gin.ReleaseMode)
	a := &API{tokens: tokens, stock: stock, orders: orders, log: log}

	r := gin.New()
	r.Use(gin.Recovery(), a.accessLog)
	r.GET("/health", func(c *gin.Context) { c.JSON(http.StatusOK, gin.H{"status": "ok"}) })
	// depuração, sem token: ver o estado de todos os pedidos de uma vez
	r.GET("/sim/orders", func(c *gin.Context) { ok(c, a.orders.List()) })

	v1 := r.Group("/api2.0/v1")
	v1.POST("/authentication/getAccessToken", tokens.getAccessToken)

	authed := v1.Group("", tokens.Require)
	authed.GET("/product/list", a.productList)
	authed.GET("/product/stock/queryByVid", a.stockByVID)
	authed.POST("/shopping/order/createOrderV2", a.createOrder)
	authed.GET("/shopping/order/getOrderDetail", a.orderDetail)
	authed.DELETE("/shopping/order/deleteOrder", a.deleteOrder)
	return r
}

// accessLog registra cada chamada; o /health fica de fora para não poluir com o healthcheck.
func (a *API) accessLog(c *gin.Context) {
	start := time.Now()
	c.Next()
	if c.FullPath() == "/health" {
		return
	}
	a.log.Info("requisição", "method", c.Request.Method, "path", c.Request.URL.Path,
		"status", c.Writer.Status(), "duration_ms", time.Since(start).Milliseconds())
}

func (a *API) productList(c *gin.Context) {
	pageNum := queryInt(c, "pageNum", 1)
	pageSize := min(queryInt(c, "pageSize", 20), maxPageSize)
	list, total := catalog.Page(pageNum, pageSize)
	ok(c, gin.H{"pageNum": pageNum, "pageSize": pageSize, "total": total, "list": list})
}

type stockEntry struct {
	VID        string `json:"vid"`
	AreaID     string `json:"areaId"`
	AreaEn     string `json:"areaEn"`
	StorageNum int    `json:"storageNum"`
}

func (a *API) stockByVID(c *gin.Context) {
	vid := c.Query("vid")
	if vid == "" {
		fail(c, http.StatusOK, CodeInvalidParams, "Invalid params")
		return
	}
	// um só armazém: o simulador finge que tudo sai do Brasil
	ok(c, []stockEntry{{VID: vid, AreaID: "BR", AreaEn: "Brazil Warehouse", StorageNum: a.stock.Available(vid)}})
}

type createResponse struct {
	OrderID       string `json:"orderId"`
	OrderNumber   string `json:"orderNumber"`
	OrderStatus   string `json:"orderStatus"`
	OrderAmount   string `json:"orderAmount"`
	ProductAmount string `json:"productAmount"`
}

func (a *API) createOrder(c *gin.Context) {
	var req order.CreateRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		fail(c, http.StatusOK, CodeInvalidParams, "Invalid params")
		return
	}
	o, err := a.orders.Create(req)
	if err != nil {
		a.orderError(c, err)
		return
	}
	a.log.Info("pedido criado", "order_id", o.OrderID, "order_number", o.OrderNumber, "status", o.OrderStatus)
	// pedido repetido volta com o status atual, não necessariamente CREATED
	ok(c, createResponse{OrderID: o.OrderID, OrderNumber: o.OrderNumber, OrderStatus: o.OrderStatus,
		OrderAmount: o.OrderAmount, ProductAmount: o.ProductAmount})
}

func (a *API) orderDetail(c *gin.Context) {
	o, err := a.orders.Get(c.Query("orderId"))
	if err != nil {
		a.orderError(c, err)
		return
	}
	ok(c, o)
}

func (a *API) deleteOrder(c *gin.Context) {
	o, err := a.orders.Cancel(c.Query("orderId"))
	if err != nil {
		a.orderError(c, err)
		return
	}
	a.log.Info("pedido cancelado pela loja", "order_id", o.OrderID)
	ok(c, o)
}

func (a *API) orderError(c *gin.Context, err error) {
	var oos *order.OutOfStockError
	switch {
	case errors.As(err, &oos):
		fail(c, http.StatusOK, CodeOutOfStock, oos.Error())
	case errors.Is(err, order.ErrRejected):
		fail(c, http.StatusOK, CodeRejected, "Order rejected by supplier")
	case errors.Is(err, order.ErrInvalidParams):
		fail(c, http.StatusOK, CodeInvalidParams, "Invalid params")
	case errors.Is(err, order.ErrNotFound):
		fail(c, http.StatusOK, CodeNotFound, "Order not found")
	case errors.Is(err, order.ErrAlreadyShipped):
		fail(c, http.StatusOK, CodeShipped, "Order already shipped")
	default:
		a.log.Error("erro inesperado", "error", err.Error())
		fail(c, http.StatusInternalServerError, http.StatusInternalServerError, "Internal error")
	}
}

// queryInt lê um inteiro positivo da query; ausente ou inválido vira o padrão.
func queryInt(c *gin.Context, name string, def int) int {
	n, err := strconv.Atoi(c.Query(name))
	if err != nil || n < 1 {
		return def
	}
	return n
}
