package nfe

import (
	"bytes"
	"encoding/xml"
	"fmt"
	"strings"
)

// XML devolve o nfeProc (a NF-e com o protocolo de autorização), como o fornecedor e o cliente recebem.
// É o XML do simulador: tem as tags e os valores da nota, mas não é assinado.
func XML(inv Invoice, req Request) ([]byte, error) {
	n := req.InfNFe
	type prod struct {
		CProd  string `xml:"cProd"`
		CEAN   string `xml:"cEAN"`
		XProd  string `xml:"xProd"`
		NCM    string `xml:"NCM"`
		CFOP   string `xml:"CFOP"`
		UCom   string `xml:"uCom"`
		QCom   string `xml:"qCom"`
		VUnCom string `xml:"vUnCom"`
		VProd  string `xml:"vProd"`
	}
	type det struct {
		NItem int  `xml:"nItem,attr"`
		Prod  prod `xml:"prod"`
	}
	type ender struct {
		XLgr    string `xml:"xLgr"`
		Nro     string `xml:"nro"`
		XCpl    string `xml:"xCpl,omitempty"`
		XBairro string `xml:"xBairro"`
		XMun    string `xml:"xMun"`
		UF      string `xml:"UF"`
		CEP     string `xml:"CEP"`
	}
	type doc struct {
		XMLName xml.Name `xml:"nfeProc"`
		Xmlns   string   `xml:"xmlns,attr"`
		Versao  string   `xml:"versao,attr"`
		NFe     struct {
			InfNFe struct {
				ID  string `xml:"Id,attr"`
				Ide struct {
					NatOp string `xml:"natOp"`
					Mod   int    `xml:"mod"`
					Serie int    `xml:"serie"`
					NNF   int    `xml:"nNF"`
					DhEmi string `xml:"dhEmi"`
				} `xml:"ide"`
				Emit struct {
					CNPJ  string `xml:"CNPJ"`
					XNome string `xml:"xNome"`
					Ender ender  `xml:"enderEmit"`
				} `xml:"emit"`
				Dest struct {
					CPF   string `xml:"CPF"`
					XNome string `xml:"xNome"`
					Ender ender  `xml:"enderDest"`
					Email string `xml:"email,omitempty"`
				} `xml:"dest"`
				Det   []det `xml:"det"`
				Total struct {
					VProd  string `xml:"ICMSTot>vProd"`
					VFrete string `xml:"ICMSTot>vFrete"`
					VNF    string `xml:"ICMSTot>vNF"`
				} `xml:"total"`
			} `xml:"infNFe"`
		} `xml:"NFe"`
		Prot struct {
			ChNFe    string `xml:"infProt>chNFe"`
			DhRecbto string `xml:"infProt>dhRecbto"`
			NProt    string `xml:"infProt>nProt"`
			CStat    int    `xml:"infProt>cStat"`
			XMotivo  string `xml:"infProt>xMotivo"`
		} `xml:"protNFe"`
	}
	toEnder := func(e Endereco) ender {
		return ender{e.XLgr, e.Nro, e.XCpl, e.XBairro, e.XMun, e.UF, e.CEP}
	}
	var d doc
	d.Xmlns, d.Versao = "http://www.portalfiscal.inf.br/nfe", "4.00"
	inf := &d.NFe.InfNFe
	inf.ID = "NFe" + inv.Chave
	inf.Ide.NatOp, inf.Ide.Mod, inf.Ide.Serie, inf.Ide.NNF = n.Ide.NatOp, 55, inv.Serie, inv.Numero
	inf.Ide.DhEmi = inv.DataEmissao.Format("2006-01-02T15:04:05-07:00")
	inf.Emit.CNPJ, inf.Emit.XNome, inf.Emit.Ender = n.Emit.CNPJ, n.Emit.XNome, toEnder(n.Emit.EnderEmit)
	inf.Dest.CPF, inf.Dest.XNome, inf.Dest.Ender, inf.Dest.Email = n.Dest.CPF, n.Dest.XNome, toEnder(n.Dest.EnderDest), n.Dest.Email
	for _, it := range n.Det {
		p := it.Prod
		inf.Det = append(inf.Det, det{NItem: it.NItem, Prod: prod{
			p.CProd, p.CEAN, p.XProd, p.NCM, p.CFOP, p.UCom, fmt.Sprintf("%.4f", p.QCom), money(p.VUnCom), money(p.VProd),
		}})
	}
	inf.Total.VProd, inf.Total.VFrete, inf.Total.VNF = money(n.Total.ICMSTot.VProd), money(n.Total.ICMSTot.VFrete), money(n.Total.ICMSTot.VNF)
	d.Prot.ChNFe, d.Prot.NProt = inv.Chave, inv.Autorizacao.NumeroProtocolo
	d.Prot.DhRecbto = inv.Autorizacao.DataEvento.Format("2006-01-02T15:04:05-07:00")
	d.Prot.CStat, d.Prot.XMotivo = inv.Autorizacao.CodigoStatus, inv.Autorizacao.MotivoStatus

	var buf bytes.Buffer
	buf.WriteString(xml.Header)
	enc := xml.NewEncoder(&buf)
	if err := enc.Encode(d); err != nil {
		return nil, fmt.Errorf("xml: %w", err)
	}
	return buf.Bytes(), nil
}

// PDF devolve um DANFE simplificado: uma página com o emitente, o destinatário, a chave, os itens e o
// total, em PDF 1.4 escrito à mão (Helvetica, sem dependências). Serve para o cliente baixar a nota.
func PDF(inv Invoice, req Request) []byte {
	n := req.InfNFe
	lines := []string{
		"DANFE - Documento Auxiliar da Nota Fiscal Eletronica",
		fmt.Sprintf("NF-e %d  Serie %d  -  %s", inv.Numero, inv.Serie, strings.ToUpper(inv.Status)),
		"Chave de acesso: " + groupKey(inv.Chave),
		"Protocolo: " + inv.Autorizacao.NumeroProtocolo + "  " + inv.Autorizacao.DataEvento.Format("02/01/2006 15:04"),
		"",
		"Emitente: " + n.Emit.XNome + "  CNPJ " + n.Emit.CNPJ,
		"Destinatario: " + n.Dest.XNome + "  CPF " + n.Dest.CPF,
		fmt.Sprintf("%s, %s %s - %s, %s/%s - CEP %s", n.Dest.EnderDest.XLgr, n.Dest.EnderDest.Nro, n.Dest.EnderDest.XCpl,
			n.Dest.EnderDest.XBairro, n.Dest.EnderDest.XMun, n.Dest.EnderDest.UF, n.Dest.EnderDest.CEP),
		"Natureza da operacao: " + n.Ide.NatOp,
		"",
		"Itens:",
	}
	for _, d := range n.Det {
		lines = append(lines, fmt.Sprintf("  %d. %s  NCM %s  CFOP %s  %g x %s = %s", d.NItem, d.Prod.XProd, d.Prod.NCM,
			d.Prod.CFOP, d.Prod.QCom, money(d.Prod.VUnCom), money(d.Prod.VProd)))
	}
	if n.Total.ICMSTot.VFrete > 0 {
		lines = append(lines, "", "Frete: R$ "+money(n.Total.ICMSTot.VFrete))
	}
	lines = append(lines, "Valor total da nota: R$ "+money(n.Total.ICMSTot.VNF))
	if inv.Ambiente == EnvSandbox {
		lines = append(lines, "", "EMITIDA EM HOMOLOGACAO - SEM VALOR FISCAL")
	}

	var content bytes.Buffer
	content.WriteString("BT /F1 10 Tf 40 800 Td 14 TL\n")
	for _, l := range lines {
		fmt.Fprintf(&content, "(%s) Tj T*\n", pdfText(l))
	}
	content.WriteString("ET")

	objects := []string{
		"<< /Type /Catalog /Pages 2 0 R >>",
		"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
		"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
		fmt.Sprintf("<< /Length %d >>\nstream\n%s\nendstream", content.Len(), content.String()),
		"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
	}
	var out bytes.Buffer
	out.WriteString("%PDF-1.4\n")
	offsets := make([]int, len(objects))
	for k, o := range objects {
		offsets[k] = out.Len()
		fmt.Fprintf(&out, "%d 0 obj\n%s\nendobj\n", k+1, o)
	}
	xref := out.Len()
	fmt.Fprintf(&out, "xref\n0 %d\n0000000000 65535 f \n", len(objects)+1)
	for _, off := range offsets {
		fmt.Fprintf(&out, "%010d 00000 n \n", off)
	}
	fmt.Fprintf(&out, "trailer\n<< /Size %d /Root 1 0 R >>\nstartxref\n%d\n%%%%EOF\n", len(objects)+1, xref)
	return out.Bytes()
}

func money(v float64) string { return fmt.Sprintf("%.2f", v) }

func groupKey(k string) string {
	var parts []string
	for len(k) > 4 {
		parts = append(parts, k[:4])
		k = k[4:]
	}
	return strings.Join(append(parts, k), " ")
}

// pdfText escapa o texto para uma string do PDF e troca os acentos (a Helvetica padrão, em WinAnsi,
// aceita Latin-1; o resto vira "?").
func pdfText(s string) string {
	var b strings.Builder
	for _, r := range s {
		switch {
		case r == '(' || r == ')' || r == '\\':
			b.WriteByte('\\')
			b.WriteRune(r)
		case r < 128:
			b.WriteRune(r)
		case r < 256:
			fmt.Fprintf(&b, "\\%03o", r)
		default:
			b.WriteByte('?')
		}
	}
	return b.String()
}
