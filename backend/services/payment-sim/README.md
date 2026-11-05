# payment-sim

Provedor de pagamento simulado (ADR 0007, #220). Fala dois contratos: o da **Efí** (#379, o provedor da venda
de verdade; ver a seção [Efí](#efí)) e o do **Mercado Pago**, o subconjunto que o payments usava até trocar de
provedor. É dublê de teste: estado só em memória, comportamento determinístico, sobe no compose e
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
| `SIM_ADMIN_TOKEN` | (aleatório) | Abre as rotas `/sim` de operação, no header `X-Sim-Admin-Token`. Vazio: um token aleatório a cada subida, e elas ficam fechadas. |
| `SIM_WEBHOOK_URL` | vazio | Destino das notificações quando o pagamento não traz `notification_url`. |
| `SIM_WEBHOOK_SECRET` | `sim-secret` | Chave do HMAC do `x-signature`. |

## API

| Método e rota | Observação |
|---|---|
| `POST /v1/card_tokens?public_key=` | Cartão (Luhn, validade, CVV, titular) → token de uso único com os 4 últimos dígitos. |
| `POST /v1/payments` | `X-Idempotency-Key` obrigatório (a mesma chave devolve o mesmo pagamento). `pix`: exige `payer.identification` (`CPF` com 11 dígitos ou `CNPJ` com 14, só números), como o Mercado Pago; sem ele, 400. Responde `pending` com `point_of_interaction.transaction_data` (`qr_code`, `qr_code_base64`, `ticket_url`) e `date_of_expiration`. Cartão (`visa`, `master`, `amex` + `token`, `installments`, `capture`): `approved`, `authorized` (`capture: false`), `in_process` ou `rejected`. |
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

| Método e rota | Efeito | Acesso |
|---|---|---|
| `GET /sim/pix/{código}` | A página do `ticket_url`: o QR, o copia-e-cola e o botão "Pagar o PIX" (faz o papel do app do banco). | aberta, pelo código aleatório do `ticket_url` |
| `POST /sim/payments/{id}/pay` | Paga o PIX pendente. | `X-Sim-Admin-Token` |
| `GET /sim/payments` | Todos os pagamentos. | `X-Sim-Admin-Token` |
| `POST /sim/reset` | Apaga tudo. | `X-Sim-Admin-Token` |

A página do PIX abre só pelo código de 32 caracteres que vem no `ticket_url` do pagamento, e que só o comprador
recebe: o `id` é sequencial, como no Mercado Pago, e por ele qualquer um pagaria o PIX de outro (#280). As rotas de
operação pedem o header `X-Sim-Admin-Token` igual a `SIM_ADMIN_TOKEN`. Sem a variável (a demonstração pública), o
simulador gera um token aleatório ao subir, e elas ficam fechadas. No compose local, o token é `sim-admin`.

## Efí

O contrato da Efí (#379), nas duas APIs dela: **Cobranças** (o cartão) e **Pix**. Na Efí são hosts diferentes, e a
API Pix exige mTLS com o certificado `.p12` da conta; aqui é um servidor só, sem mTLS. Os formatos seguem a
documentação de dev.efipay.com.br (2026-11), inclusive as esquisitices: o 401 em texto puro, a recusa do cartão
com HTTP 200, `error_description` como texto ou objeto e a validade do PIX como número na criação e texto na
consulta.

| Variável | Padrão | Uso |
|---|---|---|
| `SIM_EFI_CLIENT_ID` / `SIM_EFI_CLIENT_SECRET` | `Client_Id_sim` / `Client_Secret_sim` | Basic do `/v1/authorize` e do `/oauth/token`. |
| `SIM_EFI_PAYEE_CODE` | `0123456789abcdef0123456789abcdef` | O identificador de conta, que o navegador manda na tokenização. |
| `SIM_EFI_PIX_KEY` | `7d9f0335-8dcc-4054-9bf9-0dbd61d36906` | A chave PIX da conta: a única que o `PUT /v2/cob` aceita. |
| `SIM_EFI_SETTLE_DELAY` | `5s` | Quanto o cartão fica `approved` antes de virar `paid`. Só o pago se estorna. |
| `SIM_EFI_REFUND_DELAY` | `1s` | Quanto um estorno ou uma devolução fica em processamento. |

### Cartão (API Cobranças)

| Método e rota | Observação |
|---|---|
| `POST /v1/authorize` | Basic + `{"grant_type":"client_credentials"}` → `access_token` (600 s). |
| `POST /v1/charge` | Primeiro passo: `items` (centavos), `shippings`, `metadata {custom_id, notification_url}` → `new`. Nada é cobrado. |
| `POST /v1/charge/:id/pay` | Segundo passo: `payment.credit_card {customer, installments, payment_token, billing_address}`. Pagar uma cobrança que já tem forma de pagamento é o erro `3500030`, então repetir não cobra duas vezes. |
| `POST /v1/charge/one-step` | Os dois numa chamada. |
| `GET /v1/charge/:id` | Com `payment.credit_card.mask` (`448578XXXXXX0087`) e o `history`. |
| `PUT /v1/charge/:id/cancel` | Só em `new`, `waiting` ou `unpaid` (`3500043`). |
| `POST /v1/charge/card/:id/refund` | `{"amount": centavos}` ou vazio (o que falta). Só em `paid`, um de cada vez e um parcial por dia, como na Efí. Responde `"Reembolso em processamento"`, sem id; o total vira `refunded`. |
| `GET /v1/notification/:token` | Todas as mudanças da cobrança (`id` ordinal, `status {current, previous}`, `value` no `paid`). |

O `customer` exige nome e sobrenome só com letras, CPF com 11 dígitos, e-mail e telefone (`^[1-9]{2}9?[0-9]{8}$`).
**O último dígito do cartão decide**, como na homologação da Efí: final 1 recusa ("Dados do cartão inválidos."),
2 recusa por segurança, 3 recusa com "tente novamente mais tarde"; os outros aprovam. A recusa responde 200 com
`status: unpaid` e `refusal {reason, retry}`. Ex.: `4485 7856 7429 0087` aprova, `4111 1111 1111 1111` recusa.

A cada mudança de status, `POST` no `notification_url` com o form `notification=<token>` (o mesmo token na vida
toda da cobrança), sem assinatura: quem recebe consulta `GET /v1/notification/:token`, como na Efí.

### PIX (API Pix)

| Método e rota | Observação |
|---|---|
| `POST /oauth/token` | Basic + `{"grant_type":"client_credentials"}` → `access_token` (3600 s). |
| `PUT /v2/cob/:txid` | `txid` com 26 a 35 alfanuméricos; `calendario.expiracao` (s), `valor.original` (`"129.90"`), `chave`, `devedor {cpf, nome}`. 201 com `loc` e `pixCopiaECola`; txid repetido é 409 `txid_duplicado`. |
| `GET /v2/cob/:txid` | Com `pix[]` (`endToEndId`, `valor`, `devolucoes[]`) depois de pago. A cobrança vencida continua `ATIVA`, mas não se paga mais. |
| `PATCH /v2/cob/:txid` | `{"status":"REMOVIDA_PELO_USUARIO_RECEBEDOR"}`: só a `ATIVA`. |
| `GET /v2/loc/:id/qrcode` | `qrcode`, `imagemQrcode` (SVG em data URI, como na Efí) e `linkVisualizacao` (a página do simulador). |
| `PUT /v2/pix/:e2eId/devolucao/:id` | `{"valor":"29.90"}`; o `id` é da loja (1 a 35 alfanuméricos), repetido é 409. R$ 0,01 é recusada (`NAO_REALIZADO`), como na homologação. |
| `GET /v2/pix/:e2eId/devolucao/:id` | |
| `PUT /v2/webhook/:chave` | `{"webhookUrl": ...}`. Como na Efí, os avisos vão para a URL + `/pix` (com `?ignorar=` no fim, o `/pix` cai dentro do parâmetro). |

O PIX pago e a devolução concluída ou recusada mandam `POST {"pix": [item]}` ao webhook da chave.

### Demonstração

| Método e rota | Efeito | Acesso |
|---|---|---|
| `POST /sim/card-token` | Faz o papel da biblioteca `payment-token-efi` (que só fala com os hosts da Efí): `{payee_code, brand, number, cvv, expiration_month, expiration_year, holder_name?, holder_document?}` → `{code: 200, data: {payment_token, card_mask}}`. CORS aberto. | aberta, pelo identificador de conta |
| `GET /sim/cob/pagar/{código}` | A página do `linkVisualizacao`: o QR, o copia-e-cola e o botão "Pagar o PIX". | aberta, pelo código aleatório |
| `GET /sim/efi/charges`, `GET /sim/efi/cobs` | Tudo. | `X-Sim-Admin-Token` |
| `POST /sim/efi/charges/:id/settle` | Liquida o cartão aprovado na hora (`paid`). | `X-Sim-Admin-Token` |
| `POST /sim/efi/charges/:id/contest` | Abre um chargeback no cartão pago (`contested`). | `X-Sim-Admin-Token` |
| `POST /sim/efi/cobs/:txid/pay` | Paga o PIX. | `X-Sim-Admin-Token` |

O `POST /sim/reset` apaga também o estado da Efí, menos os webhooks cadastrados (a loja os cadastra ao subir).
