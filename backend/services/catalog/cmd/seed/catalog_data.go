package main

// Massa de dados em português, por categoria, para a busca e o teste de carga parecerem reais.

type subcategory struct {
	slug, name string
	skuPrefix  string
	nouns      []string // início do nome do produto
	brands     []string
	attrs      []string // variações que entram no nome
	minPrice   int64    // centavos
	maxPrice   int64
}

type rootCategory struct {
	slug, name string
	children   []subcategory
}

var catalogTree = []rootCategory{
	{"eletronicos", "Eletrônicos", []subcategory{
		{"tv", "Televisores", "TV", []string{"Smart TV", "Televisor"}, []string{"Samsung", "LG", "TCL", "Philips", "Sony"},
			[]string{"32\" HD", "43\" Full HD", "50\" 4K", "55\" QLED", "65\" OLED", "75\" Neo QLED"}, 99900, 1299900},
		{"fones", "Fones de ouvido", "FONE", []string{"Fone Bluetooth", "Headset Gamer", "Fone de Ouvido"},
			[]string{"JBL", "Sony", "Edifier", "HyperX", "Xiaomi"}, []string{"sem fio", "com cancelamento de ruído", "intra-auricular", "over-ear"}, 4990, 249900},
		{"caixas-de-som", "Caixas de som", "CXS", []string{"Caixa de Som", "Soundbar"}, []string{"JBL", "Samsung", "LG", "Sony"},
			[]string{"portátil", "à prova d'água", "2.1 canais", "com subwoofer"}, 9990, 499900},
	}},
	{"informatica", "Informática", []subcategory{
		{"notebooks", "Notebooks", "NOTE", []string{"Notebook", "Notebook Gamer", "Ultrabook"}, []string{"Dell", "Lenovo", "Acer", "Asus", "Samsung"},
			[]string{"Core i5 8GB SSD 256GB", "Core i7 16GB SSD 512GB", "Ryzen 5 8GB", "Ryzen 7 16GB RTX 4060"}, 249900, 1199900},
		{"monitores", "Monitores", "MON", []string{"Monitor", "Monitor Gamer"}, []string{"LG", "Samsung", "AOC", "Dell"},
			[]string{"24\" Full HD", "27\" QHD 165Hz", "29\" Ultrawide", "32\" 4K"}, 59900, 399900},
		{"perifericos", "Periféricos", "PER", []string{"Mouse sem fio", "Teclado Mecânico", "Webcam"}, []string{"Logitech", "Redragon", "Razer", "Multilaser"},
			[]string{"RGB", "ABNT2", "Full HD", "ergonômico"}, 3990, 89900},
	}},
	{"eletrodomesticos", "Eletrodomésticos", []subcategory{
		{"geladeiras", "Geladeiras", "GEL", []string{"Geladeira", "Refrigerador"}, []string{"Brastemp", "Electrolux", "Consul", "Samsung"},
			[]string{"Frost Free 375L", "Duplex 450L", "Inverse 480L", "Side by Side 600L"}, 199900, 1099900},
		{"fogoes", "Fogões", "FOG", []string{"Fogão", "Cooktop"}, []string{"Brastemp", "Electrolux", "Atlas", "Consul"},
			[]string{"4 bocas", "5 bocas", "de indução", "com acendimento automático"}, 79900, 499900},
		{"lavadoras", "Lavadoras", "LAV", []string{"Lavadora de Roupas", "Lava e Seca"}, []string{"Brastemp", "Electrolux", "LG", "Samsung"},
			[]string{"11kg", "13kg", "15kg", "Inverter 12kg"}, 149900, 699900},
	}},
	{"casa", "Casa e móveis", []subcategory{
		{"sofas", "Sofás", "SOF", []string{"Sofá", "Sofá Retrátil", "Poltrona"}, []string{"Tok&Stok", "Mobly", "Etna"},
			[]string{"2 lugares", "3 lugares", "de canto", "em linho"}, 69900, 599900},
		{"colchoes", "Colchões", "COL", []string{"Colchão", "Cama Box"}, []string{"Ortobom", "Castor", "Simmons"},
			[]string{"Solteiro", "Casal", "Queen", "King"}, 49900, 599900},
	}},
	{"esporte", "Esporte e lazer", []subcategory{
		{"bicicletas", "Bicicletas", "BIKE", []string{"Bicicleta", "Bicicleta Elétrica"}, []string{"Caloi", "Oggi", "Sense"},
			[]string{"aro 29 21 marchas", "aro 26", "speed", "dobrável"}, 89900, 899900},
		{"fitness", "Fitness", "FIT", []string{"Esteira", "Halter", "Bicicleta Ergométrica"}, []string{"Kikos", "Movement", "Athletic"},
			[]string{"dobrável", "par 5kg", "magnética", "com inclinação"}, 4990, 599900},
	}},
}

// campanhas criadas pelo seed, relativas ao instante da carga.
type seedCampaign struct {
	slug, name      string
	discount        int
	startOffsetDays int
	durationDays    int
	share           float64 // fração dos produtos ativos elegíveis (da categoria, se houver)
	rootSlug        string  // vazio = qualquer categoria
}

var seedCampaigns = []seedCampaign{
	{"esquenta-black-friday", "Esquenta Black Friday", 10, -20, 7, 0.10, ""},
	{"black-friday", "Black Friday", 30, -1, 5, 0.15, ""},
	{"semana-da-informatica", "Semana da Informática", 15, -2, 7, 0.40, "informatica"},
	{"natal", "Natal", 20, 30, 10, 0.10, ""},
}
