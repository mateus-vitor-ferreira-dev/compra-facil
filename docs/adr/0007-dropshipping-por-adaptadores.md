# ADR 0007 — Dropshipping: fornecedor, pagamento e nota fiscal por adaptadores

- **Status:** aceito
- **Data:** 2026-10-28
- **Issues:** épico #215 (#216 a #229)

## Contexto

A loja vende do próprio estoque (o inventory reserva unidades) e cobra por um pagamento simulado. O
objetivo passa a ser vender sem estoque: o cliente paga à loja, a loja repassa o pedido a um fornecedor,
e o fornecedor envia direto ao cliente. Primeiro como portfólio, com fornecedor, pagamento e nota fiscal
simulados; depois de verdade, como renda extra, trocando os simulados pelos reais.

A pesquisa (no vault, `05-REFERENCIAS/pesquisa-dropshipping-no-brasil-em-2026-...`) apontou:

- **Fornecedor**: a API do CJ Dropshipping é a única documentada em detalhe (REST/JSON, token, estoque,
  pedido, rastreio, webhooks de `UPDATE`, pedidos de teste, valores em USD). Os nacionais (DSlite,
  Hayamax, DropNacional, Dropi) integram por API, app de plataforma ou planilha, sem contrato comum.
- **Pagamento**: PIX é o meio principal (0,99% no Mercado Pago e no Pagar.me); cartão parcelado custa
  12,9% a 13,6% em 6x. Estorno por API existe nos gateways, mas a pesquisa não trouxe os detalhes.
- **Nota fiscal**: a loja é a vendedora e emite a NF-e ao consumidor **antes do despacho** (venda à
  ordem: três notas por pedido; o CFOP da loja diverge entre as fontes, 5.120 ou 5.949). Emissores com
  API e sandbox: Nuvem Fiscal, eNotas.
- **CDC**: prazo de entrega verdadeiro (o do fornecedor mais o processamento), frete antes do pagamento,
  arrependimento em 7 dias com devolução imediata, **mesmo que o fornecedor demore**, e a loja responde
  pelo vício do produto.
- **Começar pelo nacional**: prazo de 3 a 7 dias, sem imposto de importação (as alíquotas de 2026 mudaram
  mais de uma vez) e compatível com o MEI; a intermediação internacional é vedada ao MEI.

## Decisão

Cada integração externa fica atrás de uma porta, com uma implementação simulada (a do portfólio) e, mais
tarde, as reais:

- **Fornecedor** (`ISupplierGateway`): catálogo, estoque, pedido, consulta e cancelamento; mudanças por
  webhook. O contrato segue o do CJ (status, `trackNumber`, `trackingUrl`, valores na moeda do
  fornecedor). Um serviço novo, **fulfillment**, é dono da integração: recebe os pedidos pagos, pede ao
  fornecedor e publica `SupplierOrderPlaced`, `Shipped` (com o rastreio), `Delivered` e
  `SupplierOrderFailed`. O **fornecedor simulado** é um contêiner próprio que fala o mesmo contrato e
  dispara webhooks com atraso, recusa e falta de estoque configuráveis: trocar pelo real é trocar a URL
  e o adaptador.
- **Catálogo do fornecedor**: importado por feed para o catalog, com o custo e o SKU do fornecedor
  guardados à parte do preço de venda, e o preço por regra de margem (custo convertido para BRL, mais
  margem, arredondado). O estoque do inventory passa a espelhar o do fornecedor (sincronização e
  conferência no checkout) em vez de reservar unidades próprias.
- **Pagamento** (`IPaymentProvider`): PIX (QR e expiração), cartão (autorização, captura, parcelas) e
  estorno total ou parcial, idempotente, com as taxas por meio de pagamento em configuração. Primeiro o
  simulado; depois Mercado Pago ou Pagar.me.
- **Nota fiscal** (`IInvoiceIssuer`): emitida depois do pagamento e antes do pedido ao fornecedor,
  cancelada no estorno; CFOP em configuração. Primeiro a simulada; depois Nuvem Fiscal.
- **SAGA nova**: pago → nota emitida → pedido no fornecedor → enviado → entregue. Falha do fornecedor ou
  nota recusada compensam com o estorno ao cliente. O **arrependimento** estorna o cliente na hora; a
  devolução ao fornecedor é um passo separado e assíncrono.
- **Prazo e frete** na página do produto e no checkout: o prazo do fornecedor somado ao processamento.

## Consequências

- O portfólio mostra uma SAGA mais longa e realista (com nota, fornecedor e devolução) sem conta em
  fornecedor nem dinheiro real, e os simulados viram ferramentas de teste das integrações reais.
- Vender de verdade fica a três adaptadores de distância (fornecedor nacional, gateway, emissor), mais o
  que é do dono: CNPJ (MEI ou ME, com contador), conta de recebimento no CNPJ e o fornecedor escolhido.
- O inventory muda de papel: de dono do estoque para espelho do fornecedor. O estoque próprio continua
  possível como um "fornecedor" a mais, se um dia houver produto em casa.
- Mais um serviço (fulfillment) e um contêiner de simulação no compose; no VPS, o simulado não vai.
