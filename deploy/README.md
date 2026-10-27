# Produção

A API roda no Coolify do VPS da Hetzner, e a loja no Cloudflare Workers ([ADR 0006](../docs/adr/0006-producao-no-coolify-e-cloudflare.md)).
Este guia é o da API. A loja tem o dela em [`frontend/web`](../frontend/web/README.md).

| Peça | Onde |
|---|---|
| Servidor | VPS da Hetzner `codexa-vps` (CAX21: ARM64, 8 GB), dividido com os outros projetos |
| Painel | Coolify, em <https://coolify.digitalcodexa.com> |
| API | Um recurso Docker Compose ([`coolify/compose.yaml`](coolify/compose.yaml)): gateway, os seis serviços, Postgres, Redpanda, Redis, Collector e backup |
| Imagens | GHCR (`ghcr.io/mateus-vitor-ferreira-dev/compra-facil-*`), geradas pelo CI, públicas. O VPS não compila nada |
| HTTPS | O proxy do Coolify, no domínio da API (`api.<domínio>`) |
| Backup | `pg-backup`: um `pg_dump` de cada banco por dia no R2, 14 dias |
| Observabilidade | Grafana Cloud (plano gratuito), pelo Collector |

## A stack

Tudo fica na rede desta stack, sem a rede compartilhada do Coolify ("Connect to Predefined Network"
**desligado**): o VPS roda outros projetos, e nomes como `redis` ou `postgres` se confundiriam no DNS.
Por isso o Postgres está dentro do compose, e não como banco do Coolify.

O gateway é o mesmo do compose local (as rotas e os middlewares de `infra/gateway/traefik/dynamic`),
atrás do proxy do Coolify. O rate limit usa o IP que o proxy põe no `X-Forwarded-For`, então o DNS da
API fica **sem o proxy da Cloudflare** ("DNS only"). Com o proxy laranja, todo mundo apareceria com o IP
da Cloudflare.

Memória medida com a stack parada: cerca de 1,1 GB. A soma dos tetos (`mem_limit`) é de 3,9 GB.

## 1. Contas e segredos

Antes do Coolify:

- **R2** (Cloudflare): um bucket `compra-facil-backups` (privado) e um token de API do R2 com
  leitura e escrita só nele. Anote o endpoint S3 (`https://<account id>.r2.cloudflarestorage.com`), o
  access key id e o secret.
- **Grafana Cloud**: em *Connections → OpenTelemetry (OTLP)*, o endpoint
  (`https://otlp-gateway-prod-<região>.grafana.net/otlp`), o instance id e um token com escrita de
  métricas, logs e traces. A autorização é `base64("<instance id>:<token>")`.
- **Resend**: o domínio da loja verificado (SPF e DKIM) e uma chave de API, para os e-mails.
- **GHCR**: os pacotes `compra-facil-*` públicos (*Package settings → Change visibility*).
- As senhas: `openssl rand -hex 24` para cada uma das sete do Postgres e `openssl rand -hex 32` para
  o `BFF_GATEWAY_KEY` (o mesmo valor vai para a loja).

## 2. O recurso no Coolify

**Projects → (projeto) → + New → Docker Compose** a partir deste repositório (GitHub App do Coolify).

| Campo | Valor |
|---|---|
| Branch | `main` |
| Docker Compose Location | `/deploy/coolify/compose.yaml` |
| Domínio do serviço `gateway` | `https://api.<domínio>:80` (o `:80` é a porta do contêiner) |
| Connect to Predefined Network | **Desligado** |
| Auto Deploy | **Desligado**: quem pede é o CI, depois dos testes |

**Environment Variables** (marque como segredo as senhas e chaves):

| Variável | Valor |
|---|---|
| `IMAGE_TAG` | `main`. O CI troca pela tag fixa de cada deploy |
| `FRONT_URL` | `https://<domínio>`, a loja, sem barra no fim (CORS e o link de confirmação de e-mail) |
| `BFF_GATEWAY_KEY` | o segredo do BFF |
| `POSTGRES_PASSWORD` | a do superusuário |
| `PG_CATALOG_PASSWORD`, `PG_ORDERS_PASSWORD`, `PG_INVENTORY_PASSWORD`, `PG_PAYMENTS_PASSWORD`, `PG_NOTIFICATIONS_PASSWORD`, `PG_IDENTITY_PASSWORD` | uma por serviço |
| `ADMIN_EMAIL`, `ADMIN_PASSWORD` | o operador inicial do painel. Troque a senha pelo painel depois: a variável não sobrescreve |
| `SMTP_HOST`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM` | `smtp.resend.com`, `resend`, a chave da API, `nao-responda@<domínio>` |
| `BACKUP_S3_ENDPOINT`, `BACKUP_S3_ACCESS_KEY_ID`, `BACKUP_S3_SECRET_ACCESS_KEY` | do R2 |
| `GRAFANA_CLOUD_OTLP_ENDPOINT`, `GRAFANA_CLOUD_OTLP_AUTH` | do Grafana Cloud |
| `GOOGLE_CLIENT_ID` | opcional, o login com o Google (#180) |

Opcionais: `EMAIL_CONFIRMATION_REQUIRED` (padrão `true`), `INITIAL_STOCK_FOR_NEW_PRODUCTS` (padrão
`100`: é uma loja de demonstração, o produto novo nasce com estoque), `BACKUP_S3_BUCKET`.

Sem uma das obrigatórias, o deploy para logo no começo dizendo qual falta.

## 3. DNS

Um registro `A` de `api.<domínio>` para o IP do VPS, **DNS only** na Cloudflare. No primeiro deploy, o
proxy do Coolify pede o certificado do Let's Encrypt.

## 4. Primeiro deploy

Faça o primeiro deploy pelo painel. A ordem vem do compose: o Postgres sobe, o `postgres-databases`
cria os bancos e acerta as senhas, e só então os serviços sobem. Cada um aplica as próprias migrations.

```bash
curl -s https://api.<domínio>/catalog/health
curl -s https://api.<domínio>/identity/.well-known/jwks.json
```

Os produtos vêm do seed do catalog, uma vez, pelo SSH do VPS, na rede da stack (`docker network ls`: o
nome tem o uuid do recurso):

```bash
docker run --rm --network <rede da stack> \
  -e CATALOG_DB_DSN='postgres://catalog:<senha>@postgres:5432/catalog?sslmode=disable' \
  -e CATALOG_SEED_MEDIA_URL='https://media.<domínio>' \
  ghcr.io/mateus-vitor-ferreira-dev/compra-facil-catalog-seed:main -products 2000
```

Depois disso, os deploys são do CI.

## Backup e restauração

O `pg-backup` roda às 6h UTC (3h em Brasília). Cada dia fica em `postgres/<AAAA-MM-DD>/` no bucket, com
um `.dump` por banco e os papéis em `globals.sql`. Os dias além de 14 são apagados. A cada rodada ele
manda ao Grafana Cloud as métricas `compra_facil_backup_last_success_seconds` (o horário do último
backup inteiro) e `compra_facil_backup_last_run_ok_ratio`, para o alerta de backup atrasado (#162).

Os comandos abaixo rodam no VPS, no contêiner do backup. O nome dele aparece em `docker ps`
(`pg-backup-<uuid>`).

**Um backup agora**, antes de uma mudança arriscada:

```bash
docker exec -e BACKUP_NOW=1 pg-backup-<uuid> backup.sh
```

**Um dado sumiu** (o banco está de pé). Restaure ao lado, em `<banco>_restaurado`, e copie o que falta:

```bash
docker exec -e RESTORE_SUFFIX=_restaurado pg-backup-<uuid> restore.sh 2026-10-27 orders
docker exec postgres-<uuid> psql -U postgres -d orders_restaurado   # compare e copie
docker exec postgres-<uuid> psql -U postgres -c 'DROP DATABASE orders_restaurado'
```

**O banco se perdeu.** Pare no painel os serviços que usam os bancos, restaure no lugar e suba de novo:

```bash
docker exec pg-backup-<uuid> restore.sh 2026-10-27 catalog orders inventory payments notifications identity
```

O `restore.sh` apaga e recria os objetos de cada banco a partir do dump daquele dia (`--clean`, numa
transação só). O que foi escrito depois do backup se perde: é o custo do backup diário (ADR 0006).

Os dois casos foram ensaiados com este compose, com um S3 local no lugar do R2.

## Quando algo dá errado

- **Logs**: no Grafana Cloud (Explore → Loki, por `service_name`) ou na aba Logs de cada serviço no Coolify.
- **Um serviço não sobe**: o healthcheck dele reprova, e o Coolify mostra o contêiner como *unhealthy*.
  Faltar variável aparece no começo do deploy.
- **Rate limit pegando todo mundo junto**: o DNS da API foi para o proxy da Cloudflare (laranja). Volte
  para *DNS only*.
