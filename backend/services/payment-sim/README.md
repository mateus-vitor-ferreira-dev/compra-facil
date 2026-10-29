# payment-sim

Provedor de pagamento simulado (ADR 0007, #220), no formato da API do **Mercado Pago**: o subconjunto que
o payments usa. É dublê de teste: estado só em memória, comportamento determinístico, sobe no compose e
no CI e não vai para a produção. Trocar pelo Mercado Pago de verdade é trocar a URL e as credenciais (e
conferir os campos na documentação dele: este simulador não foi validado contra a API real).

## Configuração

| Variável | Padrão | Uso |
|---|---|---|
| `PORT` | `8090` | Porta HTTP. |
| `SIM_ACCESS_TOKEN` | `TEST-sim-access-token` | `Authorization: Bearer` das rotas `/v1/payments` (o servidor da loja). |
| `SIM_PUBLIC_KEY` | `TEST-sim-public-key` | `?public_key=` do `/v1/card_tokens`. |
| `SIM_PIX_EXPIRATION` | `30m` | Validade do PIX sem `date_of_expiration` no pedido. |
| `SIM_REVIEW_DELAY` | `5s` | Quanto o cartão `CONT` fica em análise antes de aprovar. |
| `SIM_PUBLIC_URL` | `http://localhost:8092` | De onde o navegador acessa o simulador (o `ticket_url` do PIX). |
| `SIM_WEBHOOK_URL` | vazio | Destino das notificações quando o pagamento não traz `notification_url`. |
| `SIM_WEBHOOK_SECRET` | `sim-secret` | Chave do HMAC do `x-signature`. |

## API

| Método e rota | Observação |
|---|---|
| `POST /v1/card_tokens?public_key=` | Cartão (Luhn, validade, CVV, titular) → token de uso único com os 4 últimos dígitos. |
| `POST /v1/payments` | `X-Idempotency-Key` obrigatório (a mesma chave devolve o mesmo pagamento). `pix`: `pending` com `point_of_interaction.transaction_data` (`qr_code`, `qr_code_base64`, `ticket_url`) e `date_of_expiration`. Cartão (`visa`, `master`, `amex` + `token`, `installments`, `capture`): `approved`, `authorized` (`capture: false`), `in_process` ou `rejected`. |
| `GET /v1/payments/{id}` | |
| `PUT /v1/payments/{id}` | `{"capture": true}` captura a autorização; `{"status": "cancelled"}` cancela a autorização ou o PIX não pago. |
| `POST /v1/payments/{id}/refunds` | `{"amount": 20.5}` estorna parte; corpo vazio estorna o que falta. `X-Idempotency-Key` obrigatório. Todo estornado: `refunded`; parte: `approved` com `partially_refunded`. |
| `GET /v1/payments/{id}/refunds` | |

Valores em reais como número decimal (`120.9`). Erros no formato do Mercado Pago:
`{"message", "error", "status", "cause": [{"code", "description"}]}`.

### O resultado do cartão sai do nome do titular

Como nos cartões de teste do Mercado Pago: `APRO` (ou qualquer outro nome) aprova; `FUND` recusa por
saldo (`cc_rejected_insufficient_amount`); `SECU`, pelo código de segurança; `FRAU`, por risco
(`cc_rejected_high_risk`); `OTHE`, por outro motivo; `CONT` fica `in_process` e é aprovado depois de
`SIM_REVIEW_DELAY`. Qualquer número que passe no Luhn serve (ex.: `4235 6477 2802 5682`).

### PIX

O `qr_code` segue o formato EMV do BR Code (com CRC16), com recebedor e chave do simulador: não paga nada
num banco de verdade. O PIX não pago até `date_of_expiration` vira `cancelled` (`expired`).

## Webhooks

A cada mudança, `POST` para o `notification_url` do pagamento (ou `SIM_WEBHOOK_URL`), como o Mercado Pago:
`?data.id=<id>&type=payment` na URL, corpo `{"id", "type": "payment", "action": "payment.updated",
"data": {"id"}}` e os headers `x-request-id` e `x-signature: ts=<ms>,v1=<hex>`, com
`v1 = HMAC-SHA256(secret, "id:<data.id>;request-id:<x-request-id>;ts:<ts>;")`. Quem recebe consulta o
pagamento em `GET /v1/payments/{id}`. Uma fila por pagamento (na ordem), até 5 tentativas.

## Demonstração

| Método e rota | Efeito |
|---|---|
| `GET /sim/pix/{id}` | A página do `ticket_url`: o QR, o copia-e-cola e o botão "Pagar o PIX" (faz o papel do app do banco). |
| `POST /sim/payments/{id}/pay` | Paga o PIX pendente. |
| `GET /sim/payments` | Todos os pagamentos. |
| `POST /sim/reset` | Apaga tudo. |
