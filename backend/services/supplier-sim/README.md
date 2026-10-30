# supplier-sim — fornecedor simulado de dropshipping

Dublê de teste do fornecedor, com um subconjunto da **API v2 da CJ Dropshipping** (REST/JSON,
token, produtos/estoque, pedidos e webhooks de status). O serviço `fulfillment` fala com ele
pelo adaptador de fornecedor; depois, um fornecedor real entra no lugar. Estado só em memória
(reiniciar apaga pedidos e tokens) e comportamento determinístico. Não vai para a VPS.

```sh
go run ./cmd/sim                       # sobe em :8090
go test ./...
```

## Variáveis de ambiente

| Variável | Padrão | O que faz |
|---|---|---|
| `PORT` | `8090` | Porta HTTP. |
| `LOG_LEVEL` | `info` | `debug`, `info`, `warn` ou `error` (logs JSON no stdout). |
| `SIM_EMAIL` / `SIM_PASSWORD` | `sim@comprafacil.local` / `sim` | Credenciais aceitas no `getAccessToken`. |
| `SIM_OUT_OF_STOCK_VIDS` | vazio | Lista separada por vírgula de vids com estoque 0. |
| `SIM_ACCEPT_DELAY` | `2s` | CREATED → UNSHIPPED (ou → CANCELLED com `[recusar-depois]`). |
| `SIM_SHIP_DELAY` | `5s` | UNSHIPPED → SHIPPED. |
| `SIM_DELIVER_DELAY` | `10s` | SHIPPED → DELIVERED. |
| `SIM_DISPUTE_DELAY` | `5s` | Devolução aberta → resolvida (webhook `DISPUTE`). |
| `SIM_WEBHOOK_URL` | vazio | Destino dos webhooks; vazio = não envia. |
| `SIM_WEBHOOK_SECRET` | `sim-secret` | Chave do HMAC em `X-Sim-Signature`. |
| `SIM_WEBHOOK_RETRY_BASE` | `1s` | Primeira espera entre tentativas (dobra a cada uma; até 5 tentativas). |
| `SIM_WEBHOOK_DUPLICATE` | `false` | `true` envia cada webhook duas vezes (testa a idempotência de quem recebe). |

Atrasos são durações Go (`500ms`, `2s`, `1m`).

## API (prefixo `/api2.0/v1`)

Toda resposta vem no envelope da CJ: `{"code","result","message","data","requestId"}`.
Sucesso é `code 200, result true, message "Success"`. Erros de negócio voltam com **HTTP 200** e
`result false`; só autenticação usa HTTP 401.

| Método e rota | Observação |
|---|---|
| `POST /authentication/getAccessToken` | `{"email","password"}` → token com validade de 15 dias. |
| `GET /product/list?pageNum&pageSize` | 30 produtos fixos em 5 categorias (`pageSize` máx. 200). |
| `GET /product/stock/queryByVid?vid=` | Aceita qualquer vid; estoque derivado do hash (5 a 200). |
| `POST /shopping/order/createOrderV2` | `orderNumber` é a chave de idempotência. |
| `GET /shopping/order/getOrderDetail?orderId=` | |
| `DELETE /shopping/order/deleteOrder?orderId=` | Cancela em CREATED/UNSHIPPED; repetir num cancelado devolve sucesso. |

Fora do prefixo e sem token: `GET /health`, `GET /sim/orders` (depuração: todos os pedidos) e as rotas que
mudam o feed, para demonstrar e testar a importação do catálogo:

| Método e rota | Efeito |
|---|---|
| `PUT /sim/products/{vid}/cost` | `{"sellPrice":"15.90"}`: o fornecedor reajustou o custo (feed e pedidos novos). |
| `DELETE /sim/products/{vid}` | O produto sai do feed (o fornecedor deixou de vender). |
| `PUT /sim/products/{vid}/stock` | `{"stock":0}`: a disponibilidade do vid muda e sai o webhook `STOCK` (`params: vid, storageNum, updateDate`). |
| `POST /sim/catalog/reset` | Volta aos 30 produtos com os custos e o estoque de fábrica. |
As rotas do prefixo, exceto `getAccessToken`, exigem o header `CJ-Access-Token`.

| Código | Mensagem | Quando |
|---|---|---|
| 1600001 | `Invalid token` / `Invalid email or password` | Token ausente/inválido ou credencial errada (HTTP 401). |
| 1600100 | `Invalid params` | JSON inválido, sem `orderNumber`, sem produtos ou quantidade ≤ 0. |
| 1603001 | `Out of stock: <vid>` | Vid sem estoque ou quantidade acima do estoque. |
| 1603002 | `Order rejected by supplier` | `remark` contém `[recusar]`. |
| 1603003 | `Order already shipped` | `deleteOrder` depois de SHIPPED. |
| 1603404 | `Order not found` | `orderId` desconhecido (HTTP 200, como os outros erros de negócio). |

Os códigos 1603xxx são do simulador: a tabela real da CJ não foi conferida para esses casos.

Valores em dólar vêm como string decimal (`"12.40"`). O custo unitário é o `sellPrice` para os
vids do catálogo e um valor fixo derivado do hash para os demais; `orderAmount` = `productAmount`
(sem frete). O estoque não é baixado pelos pedidos.

## Ciclo de vida e marcadores no `remark`

`CREATED` →(accept)→ `UNSHIPPED`/`PROCESSING` →(ship)→ `SHIPPED` (rastreio `BR#########CJ`,
`Correios`, link do 17track) →(deliver)→ `DELIVERED`. Pedido cancelado não avança mais.

- `[recusar]`: recusa na criação (1603002), nada é gravado.
- `[recusar-depois]`: aceita e, após o accept delay, vai a `CANCELLED` com `reason: "supplier_cancelled"`.
- `[atrasar]`: fica em `UNSHIPPED` para sempre (pedido preso no fornecedor).

Cancelamento pela loja (`deleteOrder`) grava `reason: "store_cancelled"`.

## Webhooks

A cada mudança (UNSHIPPED, SHIPPED, DELIVERED, CANCELLED), `POST` em `SIM_WEBHOOK_URL`:

```json
{"messageId":"<uuid>","type":"ORDER","messageType":"UPDATE",
 "params":{"orderId","orderNumber","orderStatus","subStatus","trackNumber","trackingProvider","trackingUrl","reason","updateDate"}}
```

- `X-Sim-Signature: sha256=<hex do HMAC-SHA256 do corpo cru com SIM_WEBHOOK_SECRET>`.
  **Esquema do simulador**: a assinatura real da CJ não foi verificada; o adaptador real precisa
  seguir a documentação da CJ.
- Resposta fora de 2xx ou erro de rede: até 5 tentativas com espera exponencial, sempre com o
  mesmo `messageId` (quem recebe deduplica por ele).
- Por pedido, as mensagens saem em ordem: a seguinte só vai depois que a anterior foi entregue ou
  esgotou as tentativas. Pedidos diferentes não se bloqueiam.
