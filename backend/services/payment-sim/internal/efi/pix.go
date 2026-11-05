package efi

import (
	"encoding/base64"
	"fmt"
	"strings"
	"time"

	qrcode "github.com/skip2/go-qrcode"
)

// Status de cobrança da API Pix.
const (
	CobAtiva     = "ATIVA"
	CobConcluida = "CONCLUIDA"
	CobRemovida  = "REMOVIDA_PELO_USUARIO_RECEBEDOR"

	DevolucaoProcessando = "EM_PROCESSAMENTO"
	DevolucaoDevolvido   = "DEVOLVIDO"
	DevolucaoRecusada    = "NAO_REALIZADO"
)

type Devedor struct {
	Cpf  string `json:"cpf,omitempty"`
	Cnpj string `json:"cnpj,omitempty"`
	Nome string `json:"nome,omitempty"`
}

type InfoAdicional struct {
	Nome  string `json:"nome"`
	Valor string `json:"valor"`
}

// CobRequest é o corpo do PUT /v2/cob/:txid.
type CobRequest struct {
	Calendario *struct {
		Expiracao int `json:"expiracao"`
	} `json:"calendario"`
	Devedor *Devedor `json:"devedor"`
	Valor   *struct {
		Original Reais `json:"original"`
	} `json:"valor"`
	Chave              string          `json:"chave"`
	SolicitacaoPagador string          `json:"solicitacaoPagador"`
	InfoAdicionais     []InfoAdicional `json:"infoAdicionais"`
}

type Loc struct {
	ID       int64  `json:"id"`
	Location string `json:"location"`
	TipoCob  string `json:"tipoCob"`
}

type Horario struct {
	Solicitacao string `json:"solicitacao"`
	Liquidacao  string `json:"liquidacao,omitempty"`
}

type Devolucao struct {
	ID      string  `json:"id"`
	RtrID   string  `json:"rtrId"`
	Valor   Reais   `json:"valor"`
	Horario Horario `json:"horario"`
	Status  string  `json:"status"`
	Motivo  string  `json:"motivo,omitempty"`
}

// PixItem é um PIX recebido (o item de pix[] na cobrança e no webhook).
type PixItem struct {
	EndToEndID  string      `json:"endToEndId"`
	Txid        string      `json:"txid"`
	Chave       string      `json:"chave"`
	Valor       Reais       `json:"valor"`
	Horario     string      `json:"horario"`
	InfoPagador string      `json:"infoPagador,omitempty"`
	Devolucoes  []Devolucao `json:"devolucoes,omitempty"`
}

// Cob é uma cobrança imediata.
type Cob struct {
	Txid               string
	Status             string
	Revisao            int
	Criacao            time.Time
	Expiracao          int
	Devedor            *Devedor
	Valor              Reais
	Chave              string
	SolicitacaoPagador string
	Loc                Loc
	PixCopiaECola      string
	Pix                []PixItem

	page string
}

// Expired diz se passou da validade (a cobrança continua ATIVA, mas o banco não aceita mais o pagamento).
func (c Cob) Expired(now time.Time) bool {
	return !now.Before(c.Criacao.Add(time.Duration(c.Expiracao) * time.Second))
}

// Webhook é o cadastro de PUT /v2/webhook/:chave.
type Webhook struct {
	URL     string `json:"webhookUrl"`
	Chave   string `json:"chave"`
	Criacao string `json:"criacao"`
}

const isoMillis = "2006-01-02T15:04:05.000Z"

func iso(t time.Time) string { return t.UTC().Format(isoMillis) }

// CreateCob cria a cobrança imediata pelo txid do integrador.
func (p *Provider) CreateCob(txid string, req CobRequest) (Cob, error) {
	if !isAlnum(txid, 26, 35) {
		return Cob{}, pixErr(400, "valor_invalido", "Campo txid deve ter de 26 a 35 caracteres alfanuméricos")
	}
	if req.Calendario == nil || req.Calendario.Expiracao < 1 {
		return Cob{}, pixErr(400, "valor_invalido", "Campo calendario.expiracao deve ser maior que zero")
	}
	cents, ok := int64(0), false
	if req.Valor != nil {
		cents, ok = req.Valor.Original.Cents()
	}
	if !ok || cents <= 0 {
		return Cob{}, pixErr(400, "valor_invalido", "Campo valor.original deve ser maior que zero")
	}
	if req.Chave != p.opts.PixKey {
		return Cob{}, pixErr(400, "chave_invalida", "A chave informada não faz referência à conta Efí autenticada")
	}
	if d := req.Devedor; d != nil {
		switch {
		case d.Cpf != "" && d.Cnpj != "":
			return Cob{}, pixErr(400, "valor_invalido", "Informe devedor.cpf ou devedor.cnpj, não os dois")
		case d.Cpf != "" && (len(d.Cpf) != 11 || !digitsOnly(d.Cpf)):
			return Cob{}, pixErr(400, "valor_invalido", "Documento CPF em devedor.cpf é inválido")
		case d.Cnpj != "" && (len(d.Cnpj) != 14 || !digitsOnly(d.Cnpj)):
			return Cob{}, pixErr(400, "valor_invalido", "Documento CNPJ em devedor.cnpj é inválido")
		case d.Nome != "" && d.Cpf == "" && d.Cnpj == "":
			return Cob{}, pixErr(400, "valor_invalido", "Campo devedor.nome exige devedor.cpf ou devedor.cnpj")
		}
	}
	p.mu.Lock()
	defer p.mu.Unlock()
	if _, ok := p.cobs[txid]; ok {
		return Cob{}, pixErr(409, "txid_duplicado", "Campo txid informado já foi utilizado em outra cobrança")
	}
	p.seq++
	location := fmt.Sprintf("pix-sim.comprafacil.local/qr/v2/%s", randomHex(16))
	c := &Cob{Txid: txid, Status: CobAtiva, Criacao: p.now(), Expiracao: req.Calendario.Expiracao, Devedor: req.Devedor,
		Valor: ReaisOf(cents), Chave: req.Chave, SolicitacaoPagador: req.SolicitacaoPagador,
		Loc: Loc{ID: p.seq, Location: location, TipoCob: "cob"}, PixCopiaECola: pixCode(location), page: randomHex(16)}
	p.cobs[txid] = c
	p.locs[c.Loc.ID] = txid
	p.pages[c.page] = txid
	return p.cobCopy(c), nil
}

// Cob devolve a cobrança (GET /v2/cob/:txid).
func (p *Provider) Cob(txid string) (Cob, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	c, ok := p.cobs[txid]
	if !ok {
		return Cob{}, pixErr(404, "cobranca_nao_encontrada", "Nenhuma cobrança encontrada para o txid informado")
	}
	return p.cobCopy(c), nil
}

// Cobs lista todas (rota /sim).
func (p *Provider) Cobs() []Cob {
	p.mu.Lock()
	defer p.mu.Unlock()
	out := make([]Cob, 0, len(p.cobs))
	for _, c := range p.cobs {
		out = append(out, p.cobCopy(c))
	}
	return out
}

// RemoveCob cancela a cobrança ainda ativa (PATCH status REMOVIDA_PELO_USUARIO_RECEBEDOR).
func (p *Provider) RemoveCob(txid string) (Cob, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	c, ok := p.cobs[txid]
	if !ok {
		return Cob{}, pixErr(404, "cobranca_nao_encontrada", "Nenhuma cobrança encontrada para o txid informado")
	}
	if c.Status != CobAtiva {
		return Cob{}, pixErr(400, "status_cobranca_invalido", "A cobrança não está mais com o status ATIVA")
	}
	c.Status = CobRemovida
	c.Revisao++
	return p.cobCopy(c), nil
}

// QRCode é o GET /v2/loc/:id/qrcode: o copia-e-cola, a imagem (SVG em data URI) e a página de pagamento.
func (p *Provider) QRCode(locID int64) (code, image, link string, err error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	txid, ok := p.locs[locID]
	if !ok {
		return "", "", "", pixErr(404, "location_nao_encontrada", "Nenhuma location encontrado para o identificador informado")
	}
	c := p.cobs[txid]
	svg, err := qrSVG(c.PixCopiaECola)
	if err != nil {
		return "", "", "", err
	}
	return c.PixCopiaECola, "data:image/svg+xml;base64," + base64.StdEncoding.EncodeToString([]byte(svg)), p.opts.PayPageURL(c.page), nil
}

// ByPage acha a cobrança pelo código aleatório da página de pagamento (só o comprador o recebe).
func (p *Provider) ByPage(token string) (string, bool) {
	p.mu.Lock()
	defer p.mu.Unlock()
	txid, ok := p.pages[token]
	return txid, ok
}

// PayCob é o banco do cliente pagando o PIX: a cobrança fica CONCLUIDA e o webhook da chave é avisado.
func (p *Provider) PayCob(txid string) (Cob, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	c, ok := p.cobs[txid]
	if !ok {
		return Cob{}, pixErr(404, "cobranca_nao_encontrada", "Nenhuma cobrança encontrada para o txid informado")
	}
	if c.Status != CobAtiva || c.Expired(p.now()) {
		return Cob{}, pixErr(400, "status_cobranca_invalido", "A cobrança não pode mais ser paga")
	}
	item := PixItem{EndToEndID: "E" + randomAlnum(31), Txid: txid, Chave: c.Chave, Valor: c.Valor, Horario: iso(p.now()),
		InfoPagador: "pago no banco simulado"}
	c.Pix = append(c.Pix, item)
	c.Status = CobConcluida
	p.pixByE2E[item.EndToEndID] = txid
	p.pixChanged(c.Chave, item)
	return p.cobCopy(c), nil
}

// RequestRefund pede a devolução (PUT /v2/pix/:e2eId/devolucao/:id). O id é do integrador: repetido é 409.
// Devolução de R$ 0,01 é recusada (a regra de homologação da Efí); as outras são devolvidas depois de RefundDelay.
func (p *Provider) RequestRefund(e2e, id string, valor Reais) (Devolucao, error) {
	if !isAlnum(id, 1, 35) {
		return Devolucao{}, pixErr(400, "valor_invalido", "Campo id deve ter de 1 a 35 caracteres alfanuméricos")
	}
	cents, ok := valor.Cents()
	if !ok || cents <= 0 {
		return Devolucao{}, pixErr(400, "valor_invalido", "Campo valor deve ser maior que zero")
	}
	p.mu.Lock()
	defer p.mu.Unlock()
	c, item := p.pixLocked(e2e)
	if item == nil {
		return Devolucao{}, pixErr(404, "pix_nao_encontrado", "Nenhum pix encontrado para o identificador informado")
	}
	paid, _ := item.Valor.Cents()
	refunded := int64(0)
	for _, d := range item.Devolucoes {
		if d.ID == id {
			return Devolucao{}, pixErr(409, "devolucao_id_duplicado", "O id informado já foi utilizado em outra devolução")
		}
		if d.Status != DevolucaoRecusada {
			v, _ := d.Valor.Cents()
			refunded += v
		}
	}
	if refunded+cents > paid {
		return Devolucao{}, pixErr(400, "valor_invalido", "A soma das devoluções ultrapassa o valor do Pix")
	}
	d := Devolucao{ID: id, RtrID: "D" + randomAlnum(31), Valor: ReaisOf(cents), Horario: Horario{Solicitacao: iso(p.now())},
		Status: DevolucaoProcessando}
	item.Devolucoes = append(item.Devolucoes, d)
	txid := c.Txid
	p.schedule(p.opts.RefundDelay, func() { p.finishDevolucao(txid, e2e, id) })
	return d, nil
}

func (p *Provider) finishDevolucao(txid, e2e, id string) {
	p.mu.Lock()
	defer p.mu.Unlock()
	c, item := p.pixLocked(e2e)
	if c == nil || c.Txid != txid {
		return
	}
	for i := range item.Devolucoes {
		d := &item.Devolucoes[i]
		if d.ID != id || d.Status != DevolucaoProcessando {
			continue
		}
		if d.Valor == "0.01" {
			d.Status, d.Motivo = DevolucaoRecusada, "Devolução recusada no ambiente de homologação"
		} else {
			d.Status, d.Horario.Liquidacao = DevolucaoDevolvido, iso(p.now())
		}
		notice := *item
		notice.Devolucoes = []Devolucao{*d}
		p.pixChanged(c.Chave, notice)
	}
}

// Devolucao devolve a devolução (GET /v2/pix/:e2eId/devolucao/:id).
func (p *Provider) Devolucao(e2e, id string) (Devolucao, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	_, item := p.pixLocked(e2e)
	if item == nil {
		return Devolucao{}, pixErr(404, "pix_nao_encontrado", "Nenhum pix encontrado para o identificador informado")
	}
	for _, d := range item.Devolucoes {
		if d.ID == id {
			return d, nil
		}
	}
	return Devolucao{}, pixErr(404, "devolucao_nao_encontrada", "Nenhuma devolução encontrada para o identificador informado")
}

func (p *Provider) pixLocked(e2e string) (*Cob, *PixItem) {
	txid, ok := p.pixByE2E[e2e]
	if !ok {
		return nil, nil
	}
	c := p.cobs[txid]
	for i := range c.Pix {
		if c.Pix[i].EndToEndID == e2e {
			return c, &c.Pix[i]
		}
	}
	return nil, nil
}

// SetWebhook cadastra a URL que recebe os PIX da chave (PUT /v2/webhook/:chave).
func (p *Provider) SetWebhook(chave, url string) (Webhook, error) {
	if chave != p.opts.PixKey {
		return Webhook{}, pixErr(400, "chave_invalida", "A chave informada não faz referência à conta Efí autenticada")
	}
	if !strings.HasPrefix(url, "http://") && !strings.HasPrefix(url, "https://") {
		return Webhook{}, pixErr(400, "valor_invalido", "URL inválida")
	}
	p.mu.Lock()
	defer p.mu.Unlock()
	w := Webhook{URL: url, Chave: chave, Criacao: iso(p.now())}
	p.webhooks[chave] = w
	return w, nil
}

// GetWebhook devolve o cadastro da chave.
func (p *Provider) GetWebhook(chave string) (Webhook, bool) {
	p.mu.Lock()
	defer p.mu.Unlock()
	w, ok := p.webhooks[chave]
	return w, ok
}

// pixChanged avisa o webhook da chave. Como na Efí, "/pix" é acrescentado à URL cadastrada (com "?ignorar=" no
// fim, ele vai parar dentro do parâmetro e o caminho não muda).
func (p *Provider) pixChanged(chave string, item PixItem) {
	w, ok := p.webhooks[chave]
	if !ok || p.notify == nil {
		return
	}
	item.Devolucoes = append([]Devolucao(nil), item.Devolucoes...)
	p.notify.PixChanged(w.URL+"/pix", item)
}

func (p *Provider) cobCopy(c *Cob) Cob {
	out := *c
	out.Pix = make([]PixItem, len(c.Pix))
	for i, it := range c.Pix {
		it.Devolucoes = append([]Devolucao(nil), it.Devolucoes...)
		out.Pix[i] = it
	}
	return out
}

// Page devolve o código da página da cobrança (testes).
func (p *Provider) Page(txid string) string {
	p.mu.Lock()
	defer p.mu.Unlock()
	if c, ok := p.cobs[txid]; ok {
		return c.page
	}
	return ""
}

// pixCode monta o "copia e cola" de um QR dinâmico (BR Code com a URL do location e o CRC16 no fim). O
// recebedor é o simulador: o código não paga nada num banco de verdade.
func pixCode(location string) string {
	field := func(tag, value string) string { return fmt.Sprintf("%s%02d%s", tag, len(value), value) }
	account := field("00", "br.gov.bcb.pix") + field("25", location)
	payload := field("00", "01") + field("01", "12") + field("26", account) + field("52", "0000") + field("53", "986") +
		field("58", "BR") + field("59", "COMPRAFACIL SIMULADO") + field("60", "LAVRAS") + field("62", field("05", "***")) + "6304"
	return payload + fmt.Sprintf("%04X", crc16(payload))
}

// crc16 é o CRC-16/CCITT-FALSE que o BR Code usa.
func crc16(s string) uint16 {
	crc := uint16(0xFFFF)
	for i := range len(s) {
		crc ^= uint16(s[i]) << 8
		for range 8 {
			if crc&0x8000 != 0 {
				crc = crc<<1 ^ 0x1021
			} else {
				crc <<= 1
			}
		}
	}
	return crc
}

// qrSVG desenha o QR em SVG, como o imagemQrcode da Efí.
func qrSVG(code string) (string, error) {
	q, err := qrcode.New(code, qrcode.Medium)
	if err != nil {
		return "", fmt.Errorf("QR do PIX: %w", err)
	}
	bits := q.Bitmap()
	var b strings.Builder
	fmt.Fprintf(&b, `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %d %d" shape-rendering="crispEdges">`, len(bits), len(bits))
	fmt.Fprintf(&b, `<rect width="%d" height="%d" fill="#fff"/><path fill="#000" d="`, len(bits), len(bits))
	for y, row := range bits {
		for x, on := range row {
			if on {
				fmt.Fprintf(&b, "M%d %dh1v1h-1z", x, y)
			}
		}
	}
	b.WriteString(`"/></svg>`)
	return b.String(), nil
}
