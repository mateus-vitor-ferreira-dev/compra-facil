package nfe

import (
	"bytes"
	"encoding/xml"
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// Request devolve um pedido de emissão válido: o mesmo que o fulfillment manda.
func request(ref string) Request {
	return Request{
		Ambiente: EnvSandbox, Referencia: ref,
		InfNFe: InfNFe{
			Versao: "4.00",
			Ide:    Ide{CUF: 35, NatOp: "Venda de mercadoria adquirida de terceiros, remetida pelo fornecedor", Mod: 55, Serie: 1, TpNF: 1},
			Emit: Emit{CNPJ: "11222333000181", XNome: "CompraFacil Comercio LTDA", IE: "ISENTO", CRT: 1,
				EnderEmit: Endereco{XLgr: "Rua da Loja", Nro: "1", XBairro: "Centro", XMun: "Lavras", UF: "MG", CEP: "37200000"}},
			Dest: Dest{CPF: "52998224725", XNome: "Ana Souza", IndIEDest: 9, Email: "ana@exemplo.com",
				EnderDest: Endereco{XLgr: "Avenida Paulista", Nro: "1000", XCpl: "Apto 12", XBairro: "Bela Vista", XMun: "São Paulo", UF: "SP", CEP: "01310100"}},
			Det: []Det{
				{NItem: 1, Prod: Prod{CProd: "MOU-1", CEAN: "SEM GTIN", XProd: "Mouse", NCM: "84716053", CFOP: "6120", UCom: "UN", QCom: 2, VUnCom: 49.95, VProd: 99.9}},
				{NItem: 2, Prod: Prod{CProd: "TEC-1", CEAN: "SEM GTIN", XProd: "Teclado", NCM: "84716052", CFOP: "6120", UCom: "UN", QCom: 1, VUnCom: 150, VProd: 150}},
			},
			Total: Total{ICMSTot: ICMSTot{VProd: 249.9, VNF: 249.9}},
			Pag:   Pag{DetPag: []DetPag{{TPag: "17", VPag: 249.9}}},
		},
	}
}

func TestEmit_AuthorizesWithAValidAccessKey(t *testing.T) {
	i := NewIssuer(Options{})
	inv, err := i.Emit(request("pedido-1"))
	require.NoError(t, err)
	assert.Equal(t, StatusAuthorized, inv.Status)
	assert.Equal(t, 100, inv.Autorizacao.CodigoStatus)
	require.Len(t, inv.Chave, 44)
	assert.Equal(t, "31", inv.Chave[:2], "cUF do emitente (MG)")
	assert.Equal(t, "11222333000181", inv.Chave[6:20])
	assert.Equal(t, mod11(inv.Chave[:43]), int(inv.Chave[43]-'0'), "dígito verificador")
	assert.InDelta(t, 249.9, inv.ValorTotal, 0.001)

	// a mesma referência devolve a mesma nota: repetir depois de um timeout não emite duas
	again, err := i.Emit(request("pedido-1"))
	require.NoError(t, err)
	assert.Equal(t, inv.ID, again.ID)
	assert.Equal(t, []Invoice{inv}, i.FindByReferencia(EnvSandbox, "pedido-1"))
	assert.Empty(t, i.FindByReferencia(EnvProduction, "pedido-1"))

	other, err := i.Emit(request("pedido-2"))
	require.NoError(t, err)
	assert.Equal(t, inv.Numero+1, other.Numero)
}

func TestEmit_RejectsLikeTheSefaz(t *testing.T) {
	cases := []struct {
		name   string
		change func(*Request)
		code   int
	}{
		{"CPF inválido", func(r *Request) { r.InfNFe.Dest.CPF = "52998224724" }, 237},
		{"CNPJ inválido", func(r *Request) { r.InfNFe.Emit.CNPJ = "11222333000180" }, 207},
		{"NCM inexistente", func(r *Request) { r.InfNFe.Det[0].Prod.NCM = "123" }, 778},
		{"CFOP fora da operação", func(r *Request) { r.InfNFe.Det[1].Prod.CFOP = "5102" }, 543},
		{"total diferente dos itens", func(r *Request) { r.InfNFe.Total.ICMSTot.VNF = 250 }, 610},
		{"frete fora do total", func(r *Request) { r.InfNFe.Total.ICMSTot.VFrete = 24.3 }, 610},
		{"pagamento menor que o total", func(r *Request) { r.InfNFe.Pag.DetPag[0].VPag = 200 }, 866},
		{"CEP incompleto", func(r *Request) { r.InfNFe.Dest.EnderDest.CEP = "0131" }, 225},
		{"recusa de propósito", func(r *Request) { r.InfNFe.Dest.Email = "ana+recusar-nota@exemplo.com" }, 999},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			req := request("ref-" + tc.name)
			tc.change(&req)
			inv, err := NewIssuer(Options{}).Emit(req)
			require.NoError(t, err)
			assert.Equal(t, StatusRejected, inv.Status)
			assert.Equal(t, tc.code, inv.Autorizacao.CodigoStatus)
			assert.Contains(t, inv.Autorizacao.MotivoStatus, "Rejeição")
			assert.Empty(t, inv.Chave)
		})
	}
	_, err := NewIssuer(Options{}).Emit(Request{Ambiente: "teste", Referencia: "x"})
	assert.ErrorIs(t, err, ErrInvalid)
}

func TestCancel_OnlyTheAuthorized_WithinTheWindow(t *testing.T) {
	i := NewIssuer(Options{CancelWindow: time.Hour})
	inv, err := i.Emit(request("pedido-c"))
	require.NoError(t, err)

	_, err = i.Cancel(inv.ID, "curta")
	require.ErrorIs(t, err, ErrInvalid)
	evt, err := i.Cancel(inv.ID, "Pedido estornado: o fornecedor não atendeu")
	require.NoError(t, err)
	assert.Equal(t, 135, evt.CodigoStatus)
	again, err := i.Cancel(inv.ID, "Pedido estornado: o fornecedor não atendeu")
	require.NoError(t, err)
	assert.Equal(t, evt, again, "cancelar de novo devolve o mesmo evento")
	got, err := i.Get(inv.ID)
	require.NoError(t, err)
	assert.Equal(t, StatusCancelled, got.Status)

	rejected, err := i.Emit(func() Request { r := request("pedido-r"); r.InfNFe.Dest.CPF = "1"; return r }())
	require.NoError(t, err)
	_, err = i.Cancel(rejected.ID, "Pedido estornado: o fornecedor não atendeu")
	require.ErrorIs(t, err, ErrNotCancel)

	late, err := i.Emit(request("pedido-tarde"))
	require.NoError(t, err)
	i.now = func() time.Time { return time.Now().Add(2 * time.Hour) }
	_, err = i.Cancel(late.ID, "Pedido estornado: o fornecedor não atendeu")
	require.ErrorIs(t, err, ErrCancelWindow)
}

func TestDocuments_XMLAndDANFE(t *testing.T) {
	i := NewIssuer(Options{})
	inv, err := i.Emit(request("pedido-x"))
	require.NoError(t, err)
	got, req, err := i.Document(inv.ID)
	require.NoError(t, err)

	body, err := XML(got, req)
	require.NoError(t, err)
	var parsed struct {
		Chave string `xml:"protNFe>infProt>chNFe"`
		CPF   string `xml:"NFe>infNFe>dest>CPF"`
		Itens []struct {
			NCM string `xml:"prod>NCM"`
		} `xml:"NFe>infNFe>det"`
	}
	require.NoError(t, xml.Unmarshal(body, &parsed))
	assert.Equal(t, inv.Chave, parsed.Chave)
	assert.Equal(t, "52998224725", parsed.CPF)
	assert.Len(t, parsed.Itens, 2)

	pdf := PDF(got, req)
	assert.True(t, bytes.HasPrefix(pdf, []byte("%PDF-1.4")))
	assert.True(t, bytes.HasSuffix(pdf, []byte("%%EOF\n")))
	assert.Contains(t, string(pdf), "SEM VALOR FISCAL")
	assert.Contains(t, string(pdf), "S\\343o Paulo", "acentos em Latin-1")
}

func TestReset_DoesNotRepeatTheNumber(t *testing.T) {
	i := NewIssuer(Options{})
	before, err := i.Emit(request("pedido-n"))
	require.NoError(t, err)
	time.Sleep(2 * time.Millisecond)
	i.Reset()
	after, err := i.Emit(request("pedido-n"))
	require.NoError(t, err)
	assert.Greater(t, after.Numero, before.Numero)
	assert.NotEqual(t, before.ID, after.ID)
}

func TestEmit_FreightAddsToTheTotal(t *testing.T) {
	req := request("pedido-frete")
	req.InfNFe.Total.ICMSTot.VFrete = 24.3
	req.InfNFe.Total.ICMSTot.VNF = 274.2
	req.InfNFe.Pag.DetPag[0].VPag = 274.2
	inv, err := NewIssuer(Options{}).Emit(req)
	require.NoError(t, err)
	assert.Equal(t, StatusAuthorized, inv.Status)
	assert.InDelta(t, 274.2, inv.ValorTotal, 0.001)
	assert.Contains(t, string(PDF(inv, req)), "Frete: R$ 24.30")
}

func TestValidCPF(t *testing.T) {
	assert.True(t, ValidCPF("52998224725"))
	assert.False(t, ValidCPF("11111111111"))
	assert.False(t, ValidCPF("5299822472"))
}
