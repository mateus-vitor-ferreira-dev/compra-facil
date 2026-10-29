package handler

import (
	"errors"
	"fmt"
	"log/slog"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/gin-gonic/gin"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/catalog"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/freight"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/supplier-sim/internal/order"
)

const maxPageSize = 200

// StockNotifier avisa a loja da mudança de estoque (o webhook.Dispatcher; nil não avisa).
type StockNotifier interface {
	EnqueueStock(vid string, available int)
}

type API struct {
	tokens *Tokens
	stock  *catalog.Stock
	orders *order.Simulator
	notify StockNotifier
	log    *slog.Logger
}

func NewRouter(tokens *Tokens, stock *catalog.Stock, orders *order.Simulator, notify StockNotifier, log *slog.Logger) *gin.Engine {
	gin.SetMode(gin.ReleaseMode)
	a := &API{tokens: tokens, stock: stock, orders: orders, notify: notify, log: log}

	r := gin.New()
	r.Use(gin.Recovery(), a.accessLog)
	r.GET("/health", func(c *gin.Context) { c.JSON(http.StatusOK, gin.H{"status": "ok"}) })
	// depuração, sem token: ver o estado de todos os pedidos de uma vez
	r.GET("/sim/orders", func(c *gin.Context) { ok(c, a.orders.List()) })
	// demonstração e testes, sem token: o fornecedor reajusta um custo ou deixa de vender um produto
	r.PUT("/sim/products/:vid/cost", a.setCost)
	r.DELETE("/sim/products/:vid", a.removeProduct)
	r.PUT("/sim/products/:vid/stock", a.setStock)
	r.POST("/sim/catalog/reset", func(c *gin.Context) { catalog.Reset(); stock.ResetStock(); ok(c, nil) })

	v1 := r.Group("/api2.0/v1")
	v1.POST("/authentication/getAccessToken", tokens.getAccessToken)

	authed := v1.Group("", tokens.Require)
	authed.GET("/product/list", a.productList)
	authed.GET("/product/stock/queryByVid", a.stockByVID)
	authed.POST("/shopping/order/createOrderV2", a.createOrder)
	authed.GET("/shopping/order/getOrderDetail", a.orderDetail)
	authed.DELETE("/shopping/order/deleteOrder", a.deleteOrder)
	authed.POST("/logistic/freightCalculate", a.freightCalculate)
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

type freightRequest struct {
	StartCountryCode string         `json:"startCountryCode"`
	EndCountryCode   string         `json:"endCountryCode"`
	Zip              string         `json:"zip"`
	Products         []freight.Item `json:"products"`
}

// freightCalculate cota o frete do pedido ao CEP (#222), como o CJ: as opções com preço e prazo.
func (a *API) freightCalculate(c *gin.Context) {
	var req freightRequest
	if c.ShouldBindJSON(&req) != nil {
		fail(c, http.StatusOK, CodeInvalidParams, "Invalid params")
		return
	}
	options, err := freight.Quote(req.EndCountryCode, req.Zip, req.Products)
	if err != nil {
		fail(c, http.StatusOK, CodeInvalidParams, "Invalid params")
		return
	}
	ok(c, options)
}

func (a *API) productList(c *gin.Context) {
	pageNum := queryInt(c, "pageNum", 1)
	pageSize := min(queryInt(c, "pageSize", 20), maxPageSize)
	list, total := catalog.Page(pageNum, pageSize)
	ok(c, gin.H{"pageNum": pageNum, "pageSize": pageSize, "total": total, "list": list})
}

type costRequest struct {
	SellPrice string `json:"sellPrice" binding:"required"`
}

func (a *API) setCost(c *gin.Context) {
	var req costRequest
	if c.ShouldBindJSON(&req) != nil {
		fail(c, http.StatusOK, CodeInvalidParams, "Invalid params")
		return
	}
	cents, err := parseUSD(req.SellPrice)
	if err != nil || cents <= 0 {
		fail(c, http.StatusOK, CodeInvalidParams, "Invalid params")
		return
	}
	if err := catalog.SetCost(c.Param("vid"), cents); err != nil {
		fail(c, http.StatusOK, CodeInvalidParams, "Unknown product")
		return
	}
	a.log.Info("custo reajustado", "vid", c.Param("vid"), "sell_price", catalog.FormatUSD(cents))
	ok(c, gin.H{"vid": c.Param("vid"), "sellPrice": catalog.FormatUSD(cents)})
}

type stockRequest struct {
	Stock *int `json:"stock" binding:"required"`
}

// setStock fixa a disponibilidade de um vid e avisa a loja pelo webhook STOCK.
func (a *API) setStock(c *gin.Context) {
	var req stockRequest
	if c.ShouldBindJSON(&req) != nil || *req.Stock < 0 {
		fail(c, http.StatusOK, CodeInvalidParams, "Invalid params")
		return
	}
	vid := c.Param("vid")
	a.stock.SetStock(vid, *req.Stock)
	if a.notify != nil {
		a.notify.EnqueueStock(vid, *req.Stock)
	}
	a.log.Info("estoque mudou no fornecedor", "vid", vid, "stock", *req.Stock)
	ok(c, gin.H{"vid": vid, "storageNum": *req.Stock})
}

func (a *API) removeProduct(c *gin.Context) {
	if err := catalog.Remove(c.Param("vid")); err != nil {
		fail(c, http.StatusOK, CodeInvalidParams, "Unknown product")
		return
	}
	a.log.Info("produto tirado do feed", "vid", c.Param("vid"))
	ok(c, nil)
}

// parseUSD lê "12.40" (o formato dos valores da CJ) em centavos.
func parseUSD(s string) (int64, error) {
	whole, frac, found := strings.Cut(s, ".")
	if !found {
		frac = "00"
	}
	if len(frac) != 2 {
		return 0, errors.New("duas casas decimais")
	}
	w, err := strconv.ParseInt(whole, 10, 64)
	if err != nil {
		return 0, fmt.Errorf("parte inteira de %q: %w", s, err)
	}
	f, err := strconv.ParseInt(frac, 10, 64)
	if err != nil {
		return 0, fmt.Errorf("centavos de %q: %w", s, err)
	}
	return w*100 + f, nil
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
