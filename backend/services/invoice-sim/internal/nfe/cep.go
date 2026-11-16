package nfe

import "strconv"

// Address é o CepEndereco da Nuvem Fiscal (GET /cep/{cep}): o que a loja usa é o codigo_ibge.
type Address struct {
	CEP        string `json:"cep"`
	CodigoIBGE string `json:"codigo_ibge"`
	Municipio  string `json:"municipio"`
	UF         string `json:"uf"`
}

type cepRange struct {
	from, to   int // os 5 primeiros dígitos, inclusive
	uf         string
	ibge, city string
}

// cepRanges: as faixas de CEP de cada UF, com a capital como município (Lavras, da loja, à parte). O
// simulador não tem a base dos Correios: o que importa é o código IBGE bater com a UF.
var cepRanges = []cepRange{
	{37200, 37209, "MG", "3138203", "Lavras"},
	{1000, 19999, "SP", "3550308", "São Paulo"},
	{20000, 28999, "RJ", "3304557", "Rio de Janeiro"},
	{29000, 29999, "ES", "3205309", "Vitória"},
	{30000, 39999, "MG", "3106200", "Belo Horizonte"},
	{40000, 48999, "BA", "2927408", "Salvador"},
	{49000, 49999, "SE", "2800308", "Aracaju"},
	{50000, 56999, "PE", "2611606", "Recife"},
	{57000, 57999, "AL", "2704302", "Maceió"},
	{58000, 58999, "PB", "2507507", "João Pessoa"},
	{59000, 59999, "RN", "2408102", "Natal"},
	{60000, 63999, "CE", "2304400", "Fortaleza"},
	{64000, 64999, "PI", "2211001", "Teresina"},
	{65000, 65999, "MA", "2111300", "São Luís"},
	{66000, 68899, "PA", "1501402", "Belém"},
	{68900, 68999, "AP", "1600303", "Macapá"},
	{69300, 69399, "RR", "1400100", "Boa Vista"},
	{69900, 69999, "AC", "1200401", "Rio Branco"},
	{69000, 69899, "AM", "1302603", "Manaus"},
	{70000, 72799, "DF", "5300108", "Brasília"},
	{73000, 73699, "DF", "5300108", "Brasília"},
	{72800, 76799, "GO", "5208707", "Goiânia"},
	{76800, 76999, "RO", "1100205", "Porto Velho"},
	{77000, 77999, "TO", "1721000", "Palmas"},
	{78000, 78899, "MT", "5103403", "Cuiabá"},
	{79000, 79999, "MS", "5002704", "Campo Grande"},
	{80000, 87999, "PR", "4106902", "Curitiba"},
	{88000, 89999, "SC", "4205407", "Florianópolis"},
	{90000, 99999, "RS", "4314902", "Porto Alegre"},
}

// LookupCEP acha o município do CEP (8 dígitos). False fora das faixas (000xx), como a consulta da Nuvem
// Fiscal com um CEP que não existe.
func LookupCEP(cep string) (Address, bool) {
	if !digits(cep, 8) {
		return Address{}, false
	}
	prefix, _ := strconv.Atoi(cep[:5])
	for _, r := range cepRanges {
		if prefix >= r.from && prefix <= r.to {
			return Address{CEP: cep, CodigoIBGE: r.ibge, Municipio: r.city, UF: r.uf}, true
		}
	}
	return Address{}, false
}
