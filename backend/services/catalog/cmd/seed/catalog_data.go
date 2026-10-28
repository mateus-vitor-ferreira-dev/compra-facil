package main

// Massa de dados em português, por categoria, para a busca e o teste de carga parecerem reais. Três
// níveis (#210): categoria → subcategoria → tipo de produto. Os produtos ficam no tipo, que tem as
// próprias fotos; a loja filtra pela subcategoria inteira (o catalog busca a subárvore) ou pelo tipo.

type productType struct {
	slug, name string
	skuPrefix  string
	nouns      []string // início do nome do produto
	brands     []string
	attrs      []string // variações que entram no nome
	minPrice   int64    // centavos
	maxPrice   int64
}

type subcategory struct {
	slug, name string
	types      []productType
}

type rootCategory struct {
	slug, name string
	children   []subcategory
}

// s é só para a tabela abaixo caber na tela
type s = []string

var catalogTree = []rootCategory{
	{"eletronicos", "Eletrônicos", []subcategory{
		{"tv", "Televisores", []productType{
			{"smart-tvs", "Smart TVs", "TV", s{"Smart TV", "Televisor"}, s{"Samsung", "LG", "TCL", "Philips", "Sony"},
				s{"32\" HD", "43\" Full HD", "50\" 4K", "55\" QLED", "65\" OLED", "75\" Neo QLED"}, 99900, 1299900},
			{"projetores", "Projetores", "PROJ", s{"Projetor", "Projetor Portátil"}, s{"Epson", "BenQ", "Optoma", "Xiaomi"},
				s{"Full HD", "4K", "3500 lumens", "com Wi-Fi"}, 149900, 899900},
			{"suportes-de-tv", "Suportes de TV", "SUP", s{"Suporte de Parede para TV", "Pedestal para TV"}, s{"Elg", "Multivisão", "Brasforma"},
				s{"articulado 32\" a 70\"", "fixo até 85\"", "inclinável", "com prateleira"}, 4990, 79900},
		}},
		{"fones", "Fones de ouvido", []productType{
			{"fones-over-ear", "Fones over-ear", "FONE", s{"Fone de Ouvido", "Headphone"}, s{"JBL", "Sony", "Edifier", "Philips"},
				s{"Bluetooth", "com cancelamento de ruído", "dobrável", "com fio"}, 9990, 249900},
			{"fones-sem-fio", "Fones sem fio", "EARB", s{"Fone Bluetooth", "Fone TWS"}, s{"JBL", "Samsung", "Xiaomi", "Edifier"},
				s{"intra-auricular", "com estojo de carga", "à prova d'água", "com ANC"}, 4990, 199900},
			{"headsets-gamer", "Headsets gamer", "HSET", s{"Headset Gamer"}, s{"HyperX", "Logitech", "Redragon", "Razer"},
				s{"7.1 surround", "sem fio", "com microfone removível", "RGB"}, 14990, 129900},
		}},
		{"caixas-de-som", "Caixas de som", []productType{
			{"caixas-bluetooth", "Caixas bluetooth", "CXS", s{"Caixa de Som Bluetooth", "Caixa de Som Portátil"}, s{"JBL", "Sony", "Ultimate Ears", "Philco"},
				s{"à prova d'água", "20W", "40W", "com luzes"}, 9990, 249900},
			{"soundbars", "Soundbars", "SBAR", s{"Soundbar"}, s{"Samsung", "LG", "Sony", "JBL"},
				s{"2.1 canais", "com subwoofer", "Dolby Atmos", "5.1 canais"}, 49900, 499900},
			{"smart-speakers", "Smart speakers", "SPK", s{"Smart Speaker", "Caixa Inteligente"}, s{"Amazon", "Google", "Apple"},
				s{"com Alexa", "com Google Assistente", "com tela", "Wi-Fi"}, 29900, 199900},
		}},
	}},
	{"informatica", "Informática", []subcategory{
		{"notebooks", "Notebooks e tablets", []productType{
			{"notebooks-e-ultrabooks", "Notebooks", "NOTE", s{"Notebook", "Notebook Gamer", "Ultrabook"}, s{"Dell", "Lenovo", "Acer", "Asus", "Samsung"},
				s{"Core i5 8GB SSD 256GB", "Core i7 16GB SSD 512GB", "Ryzen 5 8GB", "Ryzen 7 16GB RTX 4060"}, 249900, 1199900},
			{"tablets", "Tablets", "TAB", s{"Tablet"}, s{"Samsung", "Apple", "Lenovo", "Xiaomi"},
				s{"10\" 64GB", "11\" 128GB", "com caneta", "4G"}, 99900, 699900},
		}},
		{"monitores", "Monitores", []productType{
			{"monitores-pc", "Monitores", "MON", s{"Monitor", "Monitor Gamer"}, s{"LG", "Samsung", "AOC", "Dell"},
				s{"24\" Full HD", "27\" QHD 165Hz", "29\" Ultrawide", "32\" 4K"}, 59900, 399900},
		}},
		{"perifericos", "Periféricos", []productType{
			{"mouses", "Mouses", "MOU", s{"Mouse sem Fio", "Mouse Gamer", "Mouse Ergonômico"}, s{"Logitech", "Redragon", "Razer", "Multilaser"},
				s{"16000 DPI", "RGB", "silencioso", "recarregável"}, 3990, 59900},
			{"teclados", "Teclados", "TEC", s{"Teclado Mecânico", "Teclado sem Fio"}, s{"Logitech", "Redragon", "HyperX", "Keychron"},
				s{"ABNT2", "switch azul", "switch vermelho", "compacto 60%"}, 7990, 89900},
			{"mousepads", "Mousepads", "MPAD", s{"Mousepad", "Mousepad Gamer"}, s{"Logitech", "Redragon", "HyperX", "Razer"},
				s{"Speed", "Control", "extra grande 90x40", "com apoio de pulso"}, 2990, 29900},
			{"cadeiras-gamer", "Cadeiras gamer", "CAD", s{"Cadeira Gamer", "Cadeira de Escritório"}, s{"ThunderX3", "DT3", "Cougar", "Flexform"},
				s{"reclinável 180°", "com apoio lombar", "ergonômica", "de tela mesh"}, 59900, 299900},
			{"webcams", "Webcams", "CAM", s{"Webcam"}, s{"Logitech", "Razer", "Multilaser", "Intelbras"},
				s{"Full HD", "4K", "com microfone", "com luz de anel"}, 9990, 99900},
		}},
	}},
	{"eletrodomesticos", "Eletrodomésticos", []subcategory{
		{"geladeiras", "Geladeiras e freezers", []productType{
			{"geladeiras-duplex", "Geladeiras", "GEL", s{"Geladeira", "Refrigerador"}, s{"Brastemp", "Electrolux", "Consul", "Samsung"},
				s{"Frost Free 375L", "Duplex 450L", "Inverse 480L", "Side by Side 600L"}, 199900, 1099900},
			{"freezers", "Freezers", "FRZ", s{"Freezer Horizontal", "Freezer Vertical"}, s{"Consul", "Electrolux", "Metalfrio", "Esmaltec"},
				s{"220L", "309L", "Frost Free", "dupla ação"}, 199900, 499900},
			{"frigobares", "Frigobares", "FRIG", s{"Frigobar"}, s{"Consul", "Electrolux", "Midea", "Philco"},
				s{"120L", "80L", "retrô", "com freezer"}, 89900, 249900},
		}},
		{"fogoes", "Fogões e fornos", []productType{
			{"fogoes-de-piso", "Fogões", "FOG", s{"Fogão"}, s{"Brastemp", "Electrolux", "Atlas", "Consul"},
				s{"4 bocas", "5 bocas", "com acendimento automático", "inox"}, 79900, 399900},
			{"cooktops", "Cooktops", "CKT", s{"Cooktop"}, s{"Brastemp", "Electrolux", "Fischer", "Tramontina"},
				s{"de indução 4 zonas", "a gás 5 bocas", "vitrocerâmico", "2 bocas"}, 49900, 499900},
			{"fornos-eletricos", "Fornos e micro-ondas", "FOR", s{"Forno Elétrico", "Micro-ondas"}, s{"Mueller", "Electrolux", "Panasonic", "Philco"},
				s{"44L", "32L", "de embutir", "com grill"}, 39900, 349900},
		}},
		{"lavadoras", "Lavadoras", []productType{
			{"lavadoras-de-roupa", "Lavadoras", "LAV", s{"Lavadora de Roupas"}, s{"Brastemp", "Electrolux", "LG", "Samsung"},
				s{"11kg", "13kg", "15kg", "Inverter 12kg"}, 149900, 499900},
			{"lava-e-seca", "Lava e seca", "LES", s{"Lava e Seca", "Secadora de Roupas"}, s{"LG", "Samsung", "Electrolux", "Midea"},
				s{"11kg", "12kg Inverter", "com vapor", "10,5kg"}, 249900, 699900},
		}},
	}},
	{"casa", "Casa e móveis", []subcategory{
		{"sofas", "Sofás e poltronas", []productType{
			{"sofas-retrateis", "Sofás", "SOF", s{"Sofá", "Sofá Retrátil e Reclinável"}, s{"Tok&Stok", "Mobly", "Etna"},
				s{"2 lugares", "3 lugares", "de canto", "em linho"}, 89900, 599900},
			{"poltronas", "Poltronas", "POL", s{"Poltrona", "Poltrona Decorativa"}, s{"Tok&Stok", "Mobly", "Etna"},
				s{"em veludo", "giratória", "com braços de madeira", "reclinável"}, 39900, 249900},
			{"puffs", "Puffs", "PUF", s{"Puff", "Banqueta Estofada"}, s{"Mobly", "Etna", "Tok&Stok"},
				s{"redondo", "baú", "em couro sintético", "em linho"}, 9990, 79900},
		}},
		{"colchoes", "Quarto", []productType{
			{"colchoes-de-molas", "Colchões", "COL", s{"Colchão"}, s{"Ortobom", "Castor", "Simmons"},
				s{"Solteiro", "Casal", "Queen", "King"}, 49900, 599900},
			{"camas-box", "Camas box", "BOX", s{"Cama Box", "Cama Box Baú"}, s{"Ortobom", "Castor", "Simmons"},
				s{"Solteiro", "Casal", "Queen", "King"}, 59900, 499900},
			{"travesseiros", "Travesseiros", "TRV", s{"Travesseiro"}, s{"Ortobom", "Fibrasca", "Altenburg"},
				s{"de pluma", "viscoelástico", "cervical", "antialérgico"}, 2990, 39900},
		}},
	}},
	{"esporte", "Esporte e lazer", []subcategory{
		{"bicicletas", "Ciclismo", []productType{
			{"bicicletas-urbanas", "Bicicletas", "BIKE", s{"Bicicleta", "Mountain Bike", "Bicicleta Speed"}, s{"Caloi", "Oggi", "Sense"},
				s{"aro 29 21 marchas", "aro 26", "dobrável", "aro 700"}, 89900, 599900},
			{"bicicletas-eletricas", "Bicicletas elétricas", "EBIKE", s{"Bicicleta Elétrica"}, s{"Caloi", "Sense", "Two Dogs"},
				s{"350W", "500W", "aro 29", "dobrável"}, 499900, 1499900},
			{"capacetes", "Capacetes", "CAP", s{"Capacete de Ciclismo"}, s{"Giro", "Abus", "Absolute"},
				s{"com sinalizador LED", "MTB", "urbano", "com viseira"}, 9990, 89900},
		}},
		{"fitness", "Fitness", []productType{
			{"halteres", "Halteres", "HAL", s{"Halter", "Kit de Halteres"}, s{"Kikos", "Movement", "Athletic"},
				s{"par 5kg", "par 10kg", "ajustável 20kg", "emborrachado"}, 4990, 99900},
			{"esteiras", "Esteiras", "EST", s{"Esteira Ergométrica"}, s{"Kikos", "Movement", "Athletic"},
				s{"dobrável", "com inclinação", "16 km/h", "com bluetooth"}, 149900, 599900},
			{"kettlebells", "Kettlebells", "KET", s{"Kettlebell"}, s{"Kikos", "Movement", "Proaction"},
				s{"8kg", "12kg", "16kg", "emborrachado"}, 7990, 49900},
			{"tapetes-de-yoga", "Tapetes de yoga", "YOGA", s{"Tapete de Yoga", "Colchonete"}, s{"Kikos", "Liveup", "Acte"},
				s{"6mm", "antiderrapante", "em TPE", "com alça"}, 3990, 29900},
			{"bicicletas-ergometricas", "Bicicletas ergométricas", "ERG", s{"Bicicleta Ergométrica", "Bike Spinning"}, s{"Kikos", "Movement", "Athletic"},
				s{"magnética", "horizontal", "com monitor", "roda de 13kg"}, 99900, 499900},
		}},
	}},
	{"beleza", "Beleza e cuidados", []subcategory{
		{"maquiagem", "Maquiagem", []productType{
			{"batons", "Batons", "BAT", s{"Batom", "Batom Líquido"}, s{"Boticário", "Natura", "Vult", "Ruby Rose"},
				s{"matte", "cremoso", "longa duração", "nude"}, 1990, 9990},
			{"bases-e-corretivos", "Bases e corretivos", "BASE", s{"Base Líquida", "Corretivo"}, s{"Boticário", "Natura", "Vult", "Maybelline"},
				s{"cobertura média", "alta cobertura", "FPS 30", "efeito matte"}, 2990, 19990},
			{"paletas-de-sombra", "Paletas de sombra", "SOMB", s{"Paleta de Sombras"}, s{"Ruby Rose", "Vult", "Boticário", "Océane"},
				s{"12 cores", "nude", "cintilante", "18 cores"}, 2990, 19990},
			{"mascaras-de-cilios", "Máscaras de cílios", "RIM", s{"Máscara de Cílios", "Rímel"}, s{"Maybelline", "Boticário", "Vult", "Natura"},
				s{"à prova d'água", "volume", "alongamento", "curvatura"}, 1990, 12990},
		}},
		{"skincare", "Skincare", []productType{
			{"hidratantes", "Hidratantes", "HID", s{"Hidratante Facial", "Hidratante Corporal"}, s{"Nivea", "Neutrogena", "La Roche-Posay", "Natura"},
				s{"200ml", "com ácido hialurônico", "pele seca", "FPS 15"}, 1990, 19990},
			{"protetores-solares", "Protetores solares", "FPS", s{"Protetor Solar", "Protetor Solar Facial"}, s{"Nivea", "La Roche-Posay", "Neutrogena", "Episol"},
				s{"FPS 50", "FPS 70", "toque seco", "com cor"}, 2990, 14990},
			{"sabonetes-faciais", "Sabonetes faciais", "SAB", s{"Sabonete Facial", "Gel de Limpeza"}, s{"Neutrogena", "La Roche-Posay", "Cetaphil", "Natura"},
				s{"pele oleosa", "150ml", "esfoliante", "com vitamina C"}, 1490, 9990},
		}},
		{"perfumaria", "Perfumaria", []productType{
			{"perfumes", "Perfumes", "PERF", s{"Perfume", "Eau de Parfum", "Colônia"}, s{"Boticário", "Natura", "Eudora", "Avon"},
				s{"100ml", "75ml", "feminino", "masculino"}, 4990, 79900},
		}},
		{"cabelos", "Cabelos", []productType{
			{"shampoos", "Shampoos e condicionadores", "SHP", s{"Shampoo", "Condicionador", "Kit Shampoo e Condicionador"}, s{"Pantene", "Elseve", "Seda", "Natura"},
				s{"400ml", "cabelos cacheados", "antiqueda", "hidratação"}, 1490, 9990},
			{"secadores-de-cabelo", "Secadores", "SEC", s{"Secador de Cabelo"}, s{"Philips", "Taiff", "Mondial", "Britânia"},
				s{"2000W", "íons", "com difusor", "profissional"}, 9990, 79900},
			{"chapinhas", "Chapinhas e modeladores", "CHAP", s{"Chapinha", "Modelador de Cachos"}, s{"Philips", "Taiff", "Mondial", "Britânia"},
				s{"cerâmica", "titânio", "bivolt", "230°C"}, 7990, 59900},
		}},
		{"barbear", "Barbear", []productType{
			{"barbeadores", "Barbeadores", "BARB", s{"Aparelho de Barbear", "Lâmina de Barbear"}, s{"Gillette", "Bic", "Schick"},
				s{"5 lâminas", "com 4 cargas", "descartável", "sensível"}, 990, 14990},
			{"aparadores", "Aparadores e máquinas", "APAR", s{"Aparador de Barba", "Máquina de Cortar Cabelo"}, s{"Philips", "Mondial", "Wahl", "Britânia"},
				s{"sem fio", "à prova d'água", "com 8 pentes", "recarregável"}, 7990, 49900},
		}},
	}},
	{"moda", "Moda e acessórios", []subcategory{
		{"calcados", "Calçados", []productType{
			{"tenis", "Tênis", "TEN", s{"Tênis", "Tênis de Corrida", "Tênis Casual"}, s{"Nike", "Adidas", "Olympikus", "Mizuno"},
				s{"masculino", "feminino", "branco", "preto"}, 14990, 99900},
			{"botas", "Botas", "BOT", s{"Bota", "Coturno"}, s{"Democrata", "Ferracini", "Arezzo", "Via Marte"},
				s{"em couro", "cano curto", "cano alto", "impermeável"}, 19990, 89900},
			{"sandalias", "Sandálias e chinelos", "SAND", s{"Sandália", "Chinelo", "Papete"}, s{"Havaianas", "Ipanema", "Rider", "Melissa"},
				s{"masculino", "feminino", "slide", "com tiras"}, 2990, 29990},
		}},
		{"bolsas-e-mochilas", "Bolsas e mochilas", []productType{
			{"mochilas", "Mochilas", "MOCH", s{"Mochila", "Mochila para Notebook"}, s{"Samsonite", "Swissland", "Herschel", "Nike"},
				s{"15,6\"", "impermeável", "30L", "antifurto"}, 9990, 79900},
			{"bolsas", "Bolsas", "BOLS", s{"Bolsa", "Bolsa Tiracolo", "Bolsa Tote"}, s{"Arezzo", "Schutz", "Chenson", "Santa Lolla"},
				s{"em couro", "média", "transversal", "com alça de corrente"}, 12990, 89900},
			{"malas-de-viagem", "Malas de viagem", "MALA", s{"Mala de Viagem", "Mala de Bordo"}, s{"Samsonite", "American Tourister", "Travelux"},
				s{"P 10kg", "M 23kg", "G 32kg", "com rodas 360°"}, 19990, 129900},
		}},
		{"relogios", "Relógios", []productType{
			{"relogios-analogicos", "Relógios", "REL", s{"Relógio Analógico", "Relógio Cronógrafo"}, s{"Casio", "Technos", "Orient", "Seiko"},
				s{"em aço", "pulseira de couro", "à prova d'água 100m", "masculino"}, 14990, 199900},
			{"smartwatches", "Smartwatches", "SMW", s{"Smartwatch", "Relógio Inteligente"}, s{"Samsung", "Apple", "Xiaomi", "Amazfit"},
				s{"com GPS", "44mm", "com monitor cardíaco", "LTE"}, 29990, 499900},
		}},
		{"acessorios", "Acessórios", []productType{
			{"oculos-de-sol", "Óculos de sol", "OCU", s{"Óculos de Sol"}, s{"Ray-Ban", "Chilli Beans", "Oakley", "Evoke"},
				s{"aviador", "quadrado", "polarizado", "redondo"}, 9990, 89900},
			{"bones", "Bonés", "BONE", s{"Boné", "Boné Aba Curva"}, s{"New Era", "Nike", "Adidas", "Oakley"},
				s{"ajustável", "aba reta", "trucker", "preto"}, 4990, 24990},
			{"carteiras", "Carteiras", "CART", s{"Carteira"}, s{"Fossil", "Tommy Hilfiger", "Ferracini", "Calvin Klein"},
				s{"em couro", "slim", "com porta-moedas", "RFID"}, 4990, 39990},
			{"cintos", "Cintos", "CINT", s{"Cinto"}, s{"Fossil", "Ferracini", "Calvin Klein", "Levi's"},
				s{"em couro", "reversível", "fivela prata", "casual"}, 3990, 24990},
		}},
	}},
	{"cozinha", "Casa e cozinha", []subcategory{
		{"panelas-e-frigideiras", "Panelas e frigideiras", []productType{
			{"panelas", "Panelas", "PAN", s{"Panela", "Jogo de Panelas", "Panela de Pressão"}, s{"Tramontina", "Rochedo", "Panelux", "Brinox"},
				s{"antiaderente", "inox", "5 peças", "de cerâmica"}, 7990, 99900},
			{"frigideiras", "Frigideiras", "FRIGI", s{"Frigideira", "Frigideira Wok"}, s{"Tramontina", "Rochedo", "Brinox", "Le Creuset"},
				s{"24cm", "28cm", "antiaderente", "de ferro fundido"}, 4990, 69900},
		}},
		{"eletroportateis", "Eletroportáteis", []productType{
			{"air-fryers", "Air fryers", "AIR", s{"Air Fryer", "Fritadeira Elétrica"}, s{"Mondial", "Philips Walita", "Britânia", "Electrolux"},
				s{"4L", "5,5L", "digital", "com janela"}, 29900, 99900},
			{"cafeteiras", "Cafeteiras", "CAFE", s{"Cafeteira Elétrica", "Máquina de Espresso", "Cafeteira de Cápsulas"}, s{"Nespresso", "Dolce Gusto", "Oster", "Mondial"},
				s{"15 xícaras", "19 bar", "com moedor", "programável"}, 9990, 249900},
			{"liquidificadores", "Liquidificadores", "LIQ", s{"Liquidificador", "Mixer de Mão"}, s{"Oster", "Mondial", "Philips Walita", "Arno"},
				s{"1400W", "12 velocidades", "copo de vidro", "turbo"}, 9990, 59900},
			{"batedeiras", "Batedeiras", "BATE", s{"Batedeira Planetária", "Batedeira"}, s{"KitchenAid", "Mondial", "Arno", "Oster"},
				s{"5L", "600W", "8 velocidades", "com tigela inox"}, 14990, 349900},
			{"sanduicheiras", "Sanduicheiras e grills", "SANDU", s{"Sanduicheira", "Grill Elétrico"}, s{"Mondial", "Britânia", "Cadence", "George Foreman"},
				s{"antiaderente", "2 em 1", "750W", "grande"}, 7990, 49900},
		}},
		{"utensilios", "Utensílios", []productType{
			{"facas", "Facas", "FACA", s{"Faca do Chef", "Jogo de Facas", "Faca Santoku"}, s{"Tramontina", "Zwilling", "Wüsthof", "Mundial"},
				s{"8\"", "6 peças com suporte", "em aço inox", "lâmina de cerâmica"}, 3990, 89900},
			{"tabuas", "Tábuas de corte", "TABU", s{"Tábua de Corte", "Tábua de Churrasco"}, s{"Tramontina", "Brinox", "Mundial"},
				s{"em bambu", "em madeira", "de polietileno", "40x30cm"}, 2990, 24990},
			{"potes", "Potes", "POTE", s{"Kit de Potes", "Pote Hermético"}, s{"Tupperware", "Electrolux", "Sanremo", "OXO"},
				s{"de vidro", "10 peças", "com tampa", "para mantimentos"}, 2990, 29990},
		}},
	}},
	{"games", "Games", []subcategory{
		{"consoles-e-acessorios", "Consoles e acessórios", []productType{
			{"consoles", "Consoles", "CONS", s{"Console", "Console Portátil"}, s{"Sony", "Microsoft", "Nintendo"},
				s{"1TB", "edição digital", "com 2 controles", "OLED"}, 199900, 499900},
			{"controles", "Controles", "CTRL", s{"Controle sem Fio", "Controle Pro"}, s{"Sony", "Microsoft", "Nintendo", "8BitDo"},
				s{"preto", "branco", "com vibração", "Bluetooth"}, 19990, 129900},
			{"volantes-e-acessorios", "Volantes e acessórios", "VOL", s{"Volante Gamer", "Base de Carregamento"}, s{"Logitech", "Thrustmaster", "Fanatec"},
				s{"com pedais", "force feedback", "para 2 controles", "com câmbio"}, 9990, 349900},
		}},
	}},
	{"brinquedos", "Brinquedos", []subcategory{
		{"brinquedos-e-jogos", "Brinquedos e jogos", []productType{
			{"blocos-de-montar", "Blocos de montar", "BLOC", s{"Blocos de Montar", "Kit de Construção"}, s{"Lego", "Mega Bloks", "Xalingo"},
				s{"500 peças", "1000 peças", "cidade", "+6 anos"}, 4990, 99900},
			{"jogos-de-tabuleiro", "Jogos de tabuleiro", "JOGO", s{"Jogo de Tabuleiro", "Jogo de Xadrez"}, s{"Estrela", "Grow", "Galápagos", "Hasbro"},
				s{"família", "estratégia", "2 a 6 jogadores", "de madeira"}, 4990, 39990},
			{"pelucias", "Pelúcias", "PEL", s{"Pelúcia", "Urso de Pelúcia"}, s{"Fun", "Buba", "Antialérgica"},
				s{"30cm", "50cm", "antialérgica", "macia"}, 2990, 19990},
			{"carrinhos", "Carrinhos", "CARR", s{"Carrinho", "Carrinho de Controle Remoto"}, s{"Hot Wheels", "Maisto", "Candide"},
				s{"escala 1:24", "kit com 5", "de corrida", "recarregável"}, 1990, 49900},
		}},
	}},
	{"pets", "Pets", []subcategory{
		{"acessorios-pet", "Acessórios para pets", []productType{
			{"camas-pet", "Camas", "CAMA", s{"Cama para Cachorro", "Cama para Gato"}, s{"Petz", "Chalesco", "Furacão Pet"},
				s{"P", "M", "G", "lavável"}, 4990, 39990},
			{"brinquedos-pet", "Brinquedos", "BRPET", s{"Brinquedo para Cachorro", "Brinquedo para Gato"}, s{"Kong", "Chalesco", "Petz"},
				s{"mordedor", "bolinha", "de corda", "interativo"}, 1490, 14990},
			{"coleiras", "Coleiras e guias", "COLE", s{"Coleira", "Guia", "Peitoral"}, s{"Chalesco", "Zee.Dog", "Petz"},
				s{"P", "M", "G", "refletiva"}, 1990, 19990},
			{"comedouros", "Comedouros", "COME", s{"Comedouro", "Bebedouro", "Fonte para Gatos"}, s{"Chalesco", "Petz", "Furacão Pet"},
				s{"em inox", "duplo", "elevado", "automático"}, 1990, 29990},
		}},
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
