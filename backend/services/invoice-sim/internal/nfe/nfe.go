// Package nfe é o emissor simulado: recebe o pedido de emissão no formato da Nuvem Fiscal (as tags da
// NF-e em JSON), confere o que a SEFAZ conferiria nesta loja e autoriza ou rejeita com o código dela.
// Estado só em memória.
package nfe

import (
	"crypto/rand"
	"errors"
	"fmt"
	"math/big"
	"slices"
	"strconv"
	"strings"
	"sync"
	"time"
)

// Os status da Nuvem Fiscal que a loja usa.
const (
	StatusAuthorized = "autorizado"
	StatusRejected   = "rejeitado"
	StatusCancelled  = "cancelado"

	EnvSandbox    = "homologacao"
	EnvProduction = "producao"
)

// MarkReject no e-mail do destinatário ("ana+recusar-nota@...") faz a SEFAZ simulada rejeitar a nota:
// o jeito de testar a compensação sem inventar um CPF inválido (o checkout não deixa passar).
const MarkReject = "+recusar-nota"

var (
	ErrNotFound     = errors.New("nota não encontrada")
	ErrInvalid      = errors.New("pedido de emissão inválido")
	ErrNotCancel    = errors.New("só a nota autorizada pode ser cancelada")
	ErrCancelWindow = errors.New("prazo de cancelamento vencido")
)

// --- o pedido de emissão (subconjunto do NfePedidoEmissao) ---

type Request struct {
	Ambiente   string `json:"ambiente"`
	Referencia string `json:"referencia"`
	InfNFe     InfNFe `json:"infNFe"`
}

type InfNFe struct {
	Versao  string   `json:"versao"`
	Ide     Ide      `json:"ide"`
	Emit    Emit     `json:"emit"`
	Dest    Dest     `json:"dest"`
	Det     []Det    `json:"det"`
	Total   Total    `json:"total"`
	Transp  Transp   `json:"transp"`
	Pag     Pag      `json:"pag"`
	InfAdic *InfAdic `json:"infAdic,omitempty"`
}

type Ide struct {
	CUF      int    `json:"cUF"`
	NatOp    string `json:"natOp"`
	Mod      int    `json:"mod"`
	Serie    int    `json:"serie"`
	NNF      int    `json:"nNF"`
	DhEmi    string `json:"dhEmi"`
	TpNF     int    `json:"tpNF"`
	IDDest   int    `json:"idDest"`
	CMunFG   string `json:"cMunFG"`
	TpImp    int    `json:"tpImp"`
	TpEmis   int    `json:"tpEmis"`
	FinNFe   int    `json:"finNFe"`
	IndFinal int    `json:"indFinal"`
	IndPres  int    `json:"indPres"`
	// NFref: as notas que esta referencia (a de venda, na devolução)
	NFref []NFref `json:"NFref,omitempty"`
}

// NFref aponta para outra NF-e pela chave de acesso.
type NFref struct {
	RefNFe string `json:"refNFe"`
}

// FinDevolucao é a finalidade (finNFe) da NF-e de devolução.
const FinDevolucao = 4

type Endereco struct {
	XLgr    string `json:"xLgr"`
	Nro     string `json:"nro"`
	XCpl    string `json:"xCpl,omitempty"`
	XBairro string `json:"xBairro"`
	CMun    string `json:"cMun,omitempty"`
	XMun    string `json:"xMun"`
	UF      string `json:"UF"`
	CEP     string `json:"CEP"`
	CPais   string `json:"cPais,omitempty"`
	XPais   string `json:"xPais,omitempty"`
	Fone    string `json:"fone,omitempty"`
}

type Emit struct {
	CNPJ      string   `json:"CNPJ"`
	XNome     string   `json:"xNome"`
	EnderEmit Endereco `json:"enderEmit"`
	IE        string   `json:"IE"`
	CRT       int      `json:"CRT"`
}

type Dest struct {
	CPF       string   `json:"CPF"`
	XNome     string   `json:"xNome"`
	EnderDest Endereco `json:"enderDest"`
	IndIEDest int      `json:"indIEDest"`
	Email     string   `json:"email,omitempty"`
}

type Det struct {
	NItem int  `json:"nItem"`
	Prod  Prod `json:"prod"`
}

type Prod struct {
	CProd  string  `json:"cProd"`
	CEAN   string  `json:"cEAN"`
	XProd  string  `json:"xProd"`
	NCM    string  `json:"NCM"`
	CFOP   string  `json:"CFOP"`
	UCom   string  `json:"uCom"`
	QCom   float64 `json:"qCom"`
	VUnCom float64 `json:"vUnCom"`
	VProd  float64 `json:"vProd"`
}

type Total struct {
	ICMSTot ICMSTot `json:"ICMSTot"`
}

type ICMSTot struct {
	VProd  float64 `json:"vProd"`
	VFrete float64 `json:"vFrete,omitempty"`
	VNF    float64 `json:"vNF"`
}

type Transp struct {
	ModFrete int `json:"modFrete"`
}

type Pag struct {
	DetPag []DetPag `json:"detPag"`
}

type DetPag struct {
	TPag string  `json:"tPag"`
	VPag float64 `json:"vPag"`
}

type InfAdic struct {
	InfCpl string `json:"infCpl,omitempty"`
}

// --- a nota emitida (subconjunto do Dfe da Nuvem Fiscal) ---

type Autorizacao struct {
	CodigoStatus    int       `json:"codigo_status"`
	MotivoStatus    string    `json:"motivo_status"`
	NumeroProtocolo string    `json:"numero_protocolo,omitempty"`
	DataEvento      time.Time `json:"data_evento"`
}

type Invoice struct {
	ID          string      `json:"id"`
	Ambiente    string      `json:"ambiente"`
	CreatedAt   time.Time   `json:"created_at"`
	Status      string      `json:"status"`
	Referencia  string      `json:"referencia"`
	Chave       string      `json:"chave,omitempty"`
	Numero      int         `json:"numero,omitempty"`
	Serie       int         `json:"serie"`
	DataEmissao time.Time   `json:"data_emissao"`
	ValorTotal  float64     `json:"valor_total"`
	Autorizacao Autorizacao `json:"autorizacao"`

	request Request
	cancel  *Cancellation
}

// Cancellation é o evento de cancelamento (110111), como a Nuvem Fiscal devolve.
type Cancellation struct {
	ID              string    `json:"id"`
	Status          string    `json:"status"`
	CodigoStatus    int       `json:"codigo_status"`
	MotivoStatus    string    `json:"motivo_status"`
	Justificativa   string    `json:"justificativa"`
	NumeroProtocolo string    `json:"numero_protocolo"`
	DataEvento      time.Time `json:"data_evento"`
}

type Options struct {
	// CFOPs que a loja usa (venda à ordem: 5.120 dentro do estado, 6.120 fora, ADR 0007; devolução: 1.202 e 2.202)
	AllowedCFOP []string
	// prazo para cancelar a nota autorizada (24 h na SEFAZ)
	CancelWindow time.Duration
}

type Issuer struct {
	opts Options
	now  func() time.Time

	mu           sync.Mutex
	byID         map[string]*Invoice
	byReferencia map[string]string // ambiente/referencia → id: a mesma referência devolve a mesma nota
	numero       int
}

func NewIssuer(opts Options) *Issuer {
	if len(opts.AllowedCFOP) == 0 {
		opts.AllowedCFOP = []string{"5120", "6120"}
	}
	if opts.CancelWindow == 0 {
		opts.CancelWindow = 24 * time.Hour
	}
	i := &Issuer{opts: opts, now: time.Now}
	i.Reset()
	return i
}

// Reset apaga as notas (rota /sim/reset). A numeração parte do horário: reiniciado, o simulador não repete
// o número nem o id de uma nota que a loja já gravou.
func (i *Issuer) Reset() {
	i.mu.Lock()
	defer i.mu.Unlock()
	i.byID, i.byReferencia = map[string]*Invoice{}, map[string]string{}
	i.numero = int(i.now().UnixMilli() % 100_000_000)
}

// Emit emite a nota. A mesma referência no mesmo ambiente devolve a nota já emitida (autorizada ou não):
// a loja pode repetir o pedido depois de um timeout sem emitir duas.
func (i *Issuer) Emit(req Request) (Invoice, error) {
	req.Referencia = strings.TrimSpace(req.Referencia)
	if req.Referencia == "" || (req.Ambiente != EnvSandbox && req.Ambiente != EnvProduction) {
		return Invoice{}, fmt.Errorf("%w: ambiente e referencia são obrigatórios", ErrInvalid)
	}
	i.mu.Lock()
	defer i.mu.Unlock()
	key := req.Ambiente + "/" + req.Referencia
	if id, ok := i.byReferencia[key]; ok {
		return i.byID[id].snapshot(), nil
	}
	now := i.now().UTC()
	inv := &Invoice{
		ID: "nfe_" + randomID(), Ambiente: req.Ambiente, CreatedAt: now, Referencia: req.Referencia,
		Serie: req.InfNFe.Ide.Serie, DataEmissao: now, ValorTotal: req.InfNFe.Total.ICMSTot.VNF, request: req,
	}
	if code, reason := i.validate(req); code != 0 {
		inv.Status = StatusRejected
		inv.Autorizacao = Autorizacao{CodigoStatus: code, MotivoStatus: reason, DataEvento: now}
	} else {
		i.numero++
		inv.Status, inv.Numero = StatusAuthorized, i.numero
		inv.Chave = accessKey(req, inv.Numero, now)
		inv.Autorizacao = Autorizacao{
			CodigoStatus: 100, MotivoStatus: "Autorizado o uso da NF-e",
			NumeroProtocolo: protocol(now, i.numero), DataEvento: now,
		}
	}
	i.byID[inv.ID] = inv
	i.byReferencia[key] = inv.ID
	return inv.snapshot(), nil
}

// validate faz as conferências da SEFAZ que importam aqui; devolve o cStat e o motivo da rejeição (0 = ok).
func (i *Issuer) validate(req Request) (int, string) {
	n := req.InfNFe
	switch {
	case strings.Contains(strings.ToLower(n.Dest.Email), MarkReject):
		return 999, "Rejeição: Erro não catalogado (simulado pelo destinatário)"
	case !validCNPJ(n.Emit.CNPJ):
		return 207, "Rejeição: CNPJ do emitente inválido"
	case !ValidCPF(n.Dest.CPF):
		return 237, "Rejeição: CPF do destinatário inválido"
	case strings.TrimSpace(n.Dest.XNome) == "":
		return 225, "Rejeição: Falha no Schema XML (dest/xNome)"
	case !digits(n.Dest.EnderDest.CEP, 8):
		return 225, "Rejeição: Falha no Schema XML (enderDest/CEP)"
	case !validUF(n.Dest.EnderDest.UF) || !validUF(n.Emit.EnderEmit.UF):
		return 225, "Rejeição: Falha no Schema XML (UF)"
	case len(n.Det) == 0:
		return 225, "Rejeição: Falha no Schema XML (det)"
	}
	var sum float64
	for _, d := range n.Det {
		switch {
		case !digits(d.Prod.NCM, 8):
			return 778, "Rejeição: Informado NCM inexistente"
		case !slices.Contains(i.opts.AllowedCFOP, d.Prod.CFOP):
			return 543, "Rejeição: CFOP não permitido para a operação"
		case d.Prod.QCom <= 0 || d.Prod.VProd <= 0:
			return 225, "Rejeição: Falha no Schema XML (prod)"
		}
		sum += d.Prod.VProd
	}
	// a nota vale os produtos mais o frete cobrado do cliente
	t := n.Total.ICMSTot
	if cents(sum) != cents(t.VProd) || cents(t.VProd)+cents(t.VFrete) != cents(t.VNF) {
		return 610, "Rejeição: Total da NF difere do somatório dos Valores compõe o valor Total da NF"
	}
	// a devolução aponta para a nota de venda autorizada e não recebe nada: o estorno é fora da nota
	if n.Ide.FinNFe == FinDevolucao {
		if !i.authorizedKeys(n.Ide.NFref) {
			return 267, "Rejeição: NF-e de devolução sem a referência a uma nota autorizada"
		}
		return 0, ""
	}
	var paid float64
	for _, p := range n.Pag.DetPag {
		paid += p.VPag
	}
	if cents(paid) < cents(n.Total.ICMSTot.VNF) {
		return 866, "Rejeição: Ausência de troco quando o valor dos pagamentos informados for maior que o total da nota"
	}
	return 0, ""
}

// authorizedKeys confere se há referências e se todas são de notas autorizadas. Quem chama tem o lock.
func (i *Issuer) authorizedKeys(refs []NFref) bool {
	if len(refs) == 0 {
		return false
	}
	for _, ref := range refs {
		found := false
		for _, inv := range i.byID {
			if inv.Chave == ref.RefNFe && inv.Status == StatusAuthorized {
				found = true
				break
			}
		}
		if !found {
			return false
		}
	}
	return true
}

func (i *Issuer) Get(id string) (Invoice, error) {
	i.mu.Lock()
	defer i.mu.Unlock()
	inv, ok := i.byID[id]
	if !ok {
		return Invoice{}, ErrNotFound
	}
	return inv.snapshot(), nil
}

// FindByReferencia é o GET /nfe?referencia=: a nota de um pedido, para a loja confirmar depois de um timeout.
func (i *Issuer) FindByReferencia(ambiente, referencia string) []Invoice {
	i.mu.Lock()
	defer i.mu.Unlock()
	if id, ok := i.byReferencia[ambiente+"/"+referencia]; ok {
		return []Invoice{i.byID[id].snapshot()}
	}
	return []Invoice{}
}

// Cancel registra o evento de cancelamento. Cancelar de novo devolve o mesmo evento.
func (i *Issuer) Cancel(id, justificativa string) (Cancellation, error) {
	justificativa = strings.TrimSpace(justificativa)
	if n := len([]rune(justificativa)); n < 15 || n > 255 {
		return Cancellation{}, fmt.Errorf("%w: a justificativa tem de 15 a 255 caracteres", ErrInvalid)
	}
	i.mu.Lock()
	defer i.mu.Unlock()
	inv, ok := i.byID[id]
	if !ok {
		return Cancellation{}, ErrNotFound
	}
	if inv.cancel != nil {
		return *inv.cancel, nil
	}
	if inv.Status != StatusAuthorized {
		return Cancellation{}, ErrNotCancel
	}
	now := i.now().UTC()
	if now.Sub(inv.Autorizacao.DataEvento) > i.opts.CancelWindow {
		return Cancellation{}, ErrCancelWindow
	}
	inv.cancel = &Cancellation{
		ID: "evt_" + randomID(), Status: "registrado", CodigoStatus: 135,
		MotivoStatus: "Evento registrado e vinculado a NF-e", Justificativa: justificativa,
		NumeroProtocolo: protocol(now, inv.Numero+1), DataEvento: now,
	}
	inv.Status = StatusCancelled
	return *inv.cancel, nil
}

func (i *Issuer) GetCancellation(id string) (Cancellation, error) {
	i.mu.Lock()
	defer i.mu.Unlock()
	inv, ok := i.byID[id]
	if !ok || inv.cancel == nil {
		return Cancellation{}, ErrNotFound
	}
	return *inv.cancel, nil
}

// Document devolve a nota com o pedido que a gerou (para o XML e o DANFE).
func (i *Issuer) Document(id string) (Invoice, Request, error) {
	i.mu.Lock()
	defer i.mu.Unlock()
	inv, ok := i.byID[id]
	if !ok {
		return Invoice{}, Request{}, ErrNotFound
	}
	return inv.snapshot(), inv.request, nil
}

// List devolve todas (rota /sim, para depurar), da mais nova para a mais velha.
func (i *Issuer) List() []Invoice {
	i.mu.Lock()
	defer i.mu.Unlock()
	out := make([]Invoice, 0, len(i.byID))
	for _, inv := range i.byID {
		out = append(out, inv.snapshot())
	}
	slices.SortFunc(out, func(a, b Invoice) int { return b.CreatedAt.Compare(a.CreatedAt) })
	return out
}

func (inv *Invoice) snapshot() Invoice { return *inv }

// --- regras dos documentos ---

var ufCodes = map[string]int{
	"RO": 11, "AC": 12, "AM": 13, "RR": 14, "PA": 15, "AP": 16, "TO": 17, "MA": 21, "PI": 22, "CE": 23,
	"RN": 24, "PB": 25, "PE": 26, "AL": 27, "SE": 28, "BA": 29, "MG": 31, "ES": 32, "RJ": 33, "SP": 35,
	"PR": 41, "SC": 42, "RS": 43, "MS": 50, "MT": 51, "GO": 52, "DF": 53,
}

func validUF(uf string) bool { _, ok := ufCodes[uf]; return ok }

func digits(s string, n int) bool {
	return len(s) == n && strings.Trim(s, "0123456789") == ""
}

// ValidCPF confere os dois dígitos verificadores.
func ValidCPF(cpf string) bool {
	if !digits(cpf, 11) || strings.Count(cpf, cpf[:1]) == 11 {
		return false
	}
	check := func(length int) int {
		sum := 0
		for k := range length {
			sum += int(cpf[k]-'0') * (length + 1 - k)
		}
		r := sum * 10 % 11
		if r == 10 {
			return 0
		}
		return r
	}
	return check(9) == int(cpf[9]-'0') && check(10) == int(cpf[10]-'0')
}

func validCNPJ(cnpj string) bool {
	if !digits(cnpj, 14) || strings.Count(cnpj, cnpj[:1]) == 14 {
		return false
	}
	check := func(length int) int {
		sum, w := 0, length-7
		for k := range length {
			sum += int(cnpj[k]-'0') * w
			w--
			if w < 2 {
				w = 9
			}
		}
		if r := sum % 11; r >= 2 {
			return 11 - r
		}
		return 0
	}
	return check(12) == int(cnpj[12]-'0') && check(13) == int(cnpj[13]-'0')
}

// accessKey monta a chave de acesso de 44 dígitos: cUF, AAMM, CNPJ, modelo, série, número, tipo de
// emissão, código numérico e o dígito verificador (módulo 11).
func accessKey(req Request, numero int, at time.Time) string {
	n := req.InfNFe
	code := ufCodes[n.Emit.EnderEmit.UF]
	key := fmt.Sprintf("%02d%s%s55%03d%09d1%08d", code, at.Format("0601"), n.Emit.CNPJ, n.Ide.Serie%1000, numero%1_000_000_000, numero%100_000_000)
	return key + strconv.Itoa(mod11(key))
}

func mod11(key string) int {
	sum, w := 0, 2
	for k := len(key) - 1; k >= 0; k-- {
		sum += int(key[k]-'0') * w
		if w++; w > 9 {
			w = 2
		}
	}
	if r := sum % 11; r >= 2 {
		return 11 - r
	}
	return 0
}

func protocol(at time.Time, seq int) string {
	return fmt.Sprintf("135%s%08d", at.Format("06"), seq%100_000_000)
}

func cents(v float64) int64 {
	if v < 0 {
		return int64(v*100 - 0.5)
	}
	return int64(v*100 + 0.5)
}

func randomID() string {
	const alphabet = "0123456789abcdefghijklmnopqrstuvwxyz"
	b := make([]byte, 20)
	for k := range b {
		n, err := rand.Int(rand.Reader, big.NewInt(int64(len(alphabet))))
		if err != nil {
			panic(err) // sem entropia não há o que fazer
		}
		b[k] = alphabet[n.Int64()]
	}
	return string(b)
}
