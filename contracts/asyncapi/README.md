# Contratos de eventos (AsyncAPI 3.1)

Fonte da verdade dos eventos no Kafka (Redpanda em dev). Cada serviço tem um arquivo com o que
**publica** (canal + mensagens) e o que **consome** (`$ref` para o canal do serviço dono). Enums e
`Money` vêm dos contratos REST (`../openapi`), para não existirem duas definições.

| Arquivo | Tópico que publica | Eventos | Quem consome |
|---|---|---|---|
| [`catalog.v1.yaml`](catalog.v1.yaml) | `catalog.product.v1` | `ProductCreated`, `ProductUpdated`, `ProductPriceChanged`, `ProductDeleted` | orders (snapshot), inventory |
| [`orders.v1.yaml`](orders.v1.yaml) | `orders.order.v1` | `OrderPlaced`, `OrderConfirmed`, `OrderCancelled` | inventory, payments, notifications |
| [`inventory.v1.yaml`](inventory.v1.yaml) | `inventory.reservation.v1` | `StockReserved`, `StockReservationFailed`, `StockReleased` | payments, orders |
| [`payments.v1.yaml`](payments.v1.yaml) | `payments.payment.v1` | `PaymentApproved`, `PaymentFailed` | orders |
| [`notifications.v1.yaml`](notifications.v1.yaml) | — | só consome | — |
| [`common/v1/common.yaml`](common/v1/common.yaml) | — | envelope, headers, chaves, servidores | — |

## SAGA de compra (coreografada)

```
orders      ── OrderPlaced ─────────────▶ inventory (reserva), payments (guarda o valor)
inventory   ── StockReserved ───────────▶ payments          (ou StockReservationFailed ─▶ orders)
payments    ── PaymentApproved ─────────▶ orders            (ou PaymentFailed ──────────▶ orders)
orders      ── OrderConfirmed ──────────▶ inventory (baixa definitiva), notifications
             └ OrderCancelled ──────────▶ inventory (libera a reserva → StockReleased),
                                          payments (descarta a pendência), notifications
```

- `StockReservationFailed` → `OrderCancelled` com `reason: out_of_stock` (não havia reserva).
- `PaymentFailed` → `OrderCancelled` com `reason: payment_failed` → inventory devolve o estoque
  (compensação) e publica `StockReleased`.
- O payments **junta** `OrderPlaced` (valor a cobrar) e `StockReserved` (estoque garantido) do mesmo
  `order_id` e cobra quando os dois chegaram, em qualquer ordem: são tópicos diferentes, sem garantia
  de ordem entre eles. Assim o inventory não carrega dinheiro e o payments não chama outro serviço.

## Envelope

Todo evento é um JSON com os metadados no topo e o conteúdo em `data`
([`EventEnvelope`](common/v1/common.yaml)):

```json
{
  "event_id": "7d5c9a4e-6a0b-4b8f-9f0e-2b1c3d4e5f60",
  "event_type": "StockReserved",
  "event_version": 1,
  "occurred_at": "2026-11-28T03:00:02.418Z",
  "producer": "inventory",
  "correlation_id": "01JB7Q8M4X2K9T5V3W6Y8Z0A1B",
  "causation_id": "3a9e1f20-4c5b-4d6e-8f70-91a2b3c4d5e6",
  "data": {
    "order_id": "9b2e4c1a-5d3f-4e8a-b7c6-1a2b3c4d5e6f",
    "items": [{ "product_id": "0f8fad5b-d9cb-469f-a165-70867728950e", "quantity": 2 }]
  }
}
```

| Campo | Regra |
|---|---|
| `event_id` | UUID único; chave de idempotência do consumidor |
| `event_type` / `event_version` | escolhem o handler; `event_version` é a versão do formato de `data` |
| `occurred_at` | quando o fato aconteceu (UTC), não quando o outbox publicou |
| `correlation_id` | `X-Request-ID` da requisição que iniciou o fluxo, copiado para todos os eventos derivados |
| `causation_id` | `event_id` do evento que causou este (`null` se a causa foi HTTP) |

- **Chave da mensagem** = id do agregado (`order_id` ou `product_id`): eventos do mesmo pedido/produto
  caem na mesma partição e chegam em ordem.
- **Headers**: `traceparent`/`tracestate` (W3C) levam o trace do OpenTelemetry através do broker.
- **Tópicos**: `<serviço>.<agregado>.v<N>`, 3 partições, retenção de 7 dias.
- **Consumer group** = nome do serviço (`orders`, `inventory`, `payments`, `notifications`).

## Regras para consumidores

- **Idempotência**: guarde os `event_id` processados (tabela inbox) e ignore repetidos. O Kafka entrega
  *at least once*.
- **Tolerância**: ignore campos desconhecidos em `data` e `event_type` que o serviço não trata. É isso
  que permite evolução compatível sem coordenar deploy entre os times.
- **Ordem no catálogo**: aplique um evento de produto só se `data.version` for maior que a versão já
  guardada.
- Retries, *dead letter topics* e o que fazer com mensagem inválida ficam na tarefa #80.

## Versionamento

| Mudança | Compatível? | Como fazer |
|---|---|---|
| Campo opcional novo em `data` | sim | mesmo tópico; sobe `event_version` do evento |
| Evento novo no tópico | sim | mesmo tópico; consumidores antigos o ignoram |
| Valor novo em enum | **não** para quem valida o enum | trate como quebra, ou combine com os consumidores antes |
| Remover/renomear campo, tornar obrigatório, mudar tipo ou significado | **não** | tópico `.v2` novo; o produtor publica nos dois até todos os consumidores migrarem, depois desliga o `.v1` |

`event_version` nunca desce, e um campo removido nunca volta com outro significado. Mudou o contrato,
o PR altera o YAML aqui antes (ou junto) do código.

## Comandos

```bash
task lint:asyncapi   # AsyncAPI CLI (schema e $ref) + Spectral (regras em .spectral.yaml)
```

O Spectral verifica: nome de tópico, eventos em PascalCase, propriedades em `snake_case` e que todo
evento estende o envelope. Exemplos de payload ainda não são validados por ferramenta; por isso o único
exemplo está neste README, e não nos contratos.
