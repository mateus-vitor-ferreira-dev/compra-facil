# Contratos REST (OpenAPI 3.1)

Fonte da verdade das APIs HTTP. O serviço implementa o contrato, não o contrário.

| Arquivo | Serviço | Porta dev | Prefixo no gateway |
|---|---|---|---|
| [`catalog.v1.yaml`](catalog.v1.yaml) | catalog (Go) | 8001 | `/catalog` |
| [`orders.v1.yaml`](orders.v1.yaml) | orders (.NET) | 8002 | `/orders` |
| [`inventory.v1.yaml`](inventory.v1.yaml) | inventory (.NET) | 8003 | `/inventory` |
| [`payments.v1.yaml`](payments.v1.yaml) | payments (.NET) | 8004 | `/payments` |
| [`identity.v1.yaml`](identity.v1.yaml) | identity (.NET) | 8006 | `/identity` |
| [`common/v1/common.yaml`](common/v1/common.yaml) | — | — | componentes compartilhados |

## Convenções (verificadas pelo Spectral)

- Caminhos versionados e em kebab-case: `/v1/...`; escrita administrativa em `/v1/admin/...` (JWT, role `admin`).
  Exceção: as rotas de descoberta do identity (`/.well-known/jwks.json` e `/.well-known/openid-configuration`), de caminho fixo por padrão.
- JSON, query e path em `snake_case`.
- Todo erro 4xx/5xx é `application/problem+json` (RFC 9457) com `request_id`; validação usa `ValidationProblem` (422).
- Dinheiro sempre em centavos: `{ "amount_cents": 19990, "currency": "BRL" }`.
- Paginação por cursor opaco: `?cursor=&limit=` → `page.next_cursor` / `page.has_more`.
- Criação não idempotente por natureza exige `Idempotency-Key`; atualização concorrente usa `ETag` + `If-Match`.
- O que acontece pela SAGA (reserva, cobrança) **não** vira endpoint; fica em `contracts/asyncapi/`.

## Comandos

```bash
task lint:openapi     # Spectral (regras em .spectral.yaml)
task openapi:bundle   # um arquivo por contrato em dist/, para servir no Swagger UI dos serviços
```

## Mudando um contrato

Mudança compatível (campo opcional novo, endpoint novo) entra na mesma `v1`. Remover/renomear campo, tornar
campo obrigatório ou mudar tipo é **quebra**: cria `v2` e mantém a `v1` até os consumidores migrarem.
