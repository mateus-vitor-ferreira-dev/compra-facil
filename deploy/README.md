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
| Fotos dos produtos | R2, bucket `compra-facil-media` em `media.<domínio>`, com a CDN da Cloudflare ([abaixo](#fotos-dos-produtos)) |
| Observabilidade | Grafana Cloud (plano gratuito), pelo Collector. Dashboards e alertas por e-mail em [`grafana/`](grafana/README.md) |

## A stack

Tudo fica na rede desta stack, sem a rede compartilhada do Coolify ("Connect to Predefined Network"
**desligado**): o VPS roda outros projetos, e nomes como `redis` ou `postgres` se confundiriam no DNS.
Por isso o Postgres está dentro do compose, e não como banco do Coolify.

O gateway é o mesmo do compose local (as rotas e os middlewares de `infra/gateway/traefik/dynamic`),
atrás do proxy do Coolify.

**Nenhum arquivo do repositório é montado.** O Coolify resolve os caminhos de `volumes` no disco do servidor
(`/data/coolify/...`), onde o repositório não fica, e foi isso que derrubou o primeiro deploy (#278). A configuração
vai dentro de imagens próprias, geradas pelo [`stack-images.yml`](../.github/workflows/stack-images.yml) a partir de
[`images/`](images), com as tags da branch e do commit:

| Imagem | Leva |
|---|---|
| `compra-facil-gateway` | `coolify/traefik.yml` e as rotas de `infra/gateway/traefik/dynamic` |
| `compra-facil-postgres-init` | `infra/compose/postgres/init-databases.sh` |
| `compra-facil-redpanda` | `coolify/redpanda.yaml` e `coolify/redpanda.bootstrap.yaml` |
| `compra-facil-otel-collector` | `coolify/otel-collector.yaml` |
| `compra-facil-media` | as fotos de `infra/media`, só na demonstração |

Mudou um desses arquivos: o pipeline gera a imagem de novo, e o deploy seguinte a leva. Um arquivo novo que a
stack precise vai para o Dockerfile da imagem, e nunca para um `volumes` do compose. O rate limit usa o IP que o proxy põe no `X-Forwarded-For`, então o DNS da
API fica **sem o proxy da Cloudflare** ("DNS only"). Com o proxy laranja, todo mundo apareceria com o IP
da Cloudflare.

Memória medida com a stack parada: cerca de 1,1 GB. A soma dos tetos (`mem_limit`) é de 3,9 GB.

### Memória do VPS

Medição da demonstração em 02/11 (#276): a stack usa 0,93 GB parada e 1,14 GB sob carga (k6 a 30 req/s pelo
gateway). Nenhum contêiner passou de 58% do teto. Os tetos da demonstração somam 3,65 GB, e o VPS inteiro, com os
outros projetos, ficou em 4,7 GB de 8 GB no pico. Os números vêm do Sentinel do Coolify (*Servers → localhost →
Metrics*) e do `docker stats --no-stream` no terminal do servidor.

O Coolify não tem notificação de memória. O alerta é uma tarefa agendada do recurso (*Scheduled Tasks*), que falha
quando o VPS passa de 85% e cai no e-mail de **Scheduled task failure**, já ligado nas notificações do time:

| Campo | Valor |
|---|---|
| Name | `alerta-memoria-vps` |
| Schedule | `*/15 * * * *` |
| Container | `redis` (qualquer um com `awk`; o `/proc/meminfo` do contêiner é o do servidor) |
| Timeout | `60` (o mínimo do Coolify) |

```sh
awk '/^MemTotal:/{t=$2} /^MemAvailable:/{a=$2} END{u=int(100-a*100/t); print "memoria do VPS em uso: " u "%"; if (u>=85) exit 1}' /proc/meminfo
```

O log da execução mostra a porcentagem. Para testar o e-mail, troque `85` por `1`, rode *Execute Now* e volte.
A tarefa roda dentro desta stack: se ela cair, o aviso que chega é o **Server unreachable** ou o **Restart limit
reached**.

## Loja de demonstração

Antes das contas de verdade, [`coolify/compose.demo.yaml`](coolify/compose.demo.yaml) sobe a stack com o fornecedor, o
pagamento e a nota simulados, em domínios `sslip.io`, sem R2, Resend nem Grafana. Os e-mails ficam no Mailpit, sem
chegar a ninguém. O resto deste guia é o da produção de verdade.

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

Antes do seed, as fotos no R2 ([Fotos dos produtos](#fotos-dos-produtos)): o catálogo guarda a URL de
cada uma, e uma foto que ainda não está lá aparece quebrada na loja.

Os produtos vêm do seed do catalog, uma vez, pelo SSH do VPS, na rede da stack (`docker network ls`: o
nome tem o uuid do recurso):

```bash
docker run --rm --network <rede da stack> \
  -e CATALOG_DB_DSN='postgres://catalog:<senha>@postgres:5432/catalog?sslmode=disable' \
  -e CATALOG_SEED_MEDIA_URL='https://media.<domínio>' \
  ghcr.io/mateus-vitor-ferreira-dev/compra-facil-catalog-seed:main -products 2000
```

Depois disso, os deploys são do CI.

## Fotos dos produtos

No local, as fotos saem do Caddy do compose (`/media` no gateway). Em produção a rota `/media` do gateway
fica desligada (`GATEWAY_MEDIA=off`), e elas saem do R2 pela CDN da Cloudflare.

Uma vez, na Cloudflare:

1. **R2 → Create bucket**: `compra-facil-media`.
2. No bucket, **Settings → Public access → Custom Domains**: `media.<domínio>` (a zona já na
   Cloudflare). O endereço `r2.dev` fica **desligado**: ele não passa pela CDN e tem limite de uso.
3. **R2 → Manage API tokens**: um token com *Object Read & Write* só nesse bucket (não o do backup).

Para enviar as fotos (da máquina, depois de cada `product_images.py build` que mudar alguma):

```bash
MEDIA_S3_ENDPOINT=https://<account id>.r2.cloudflarestorage.com \
MEDIA_S3_ACCESS_KEY_ID=... MEDIA_S3_SECRET_ACCESS_KEY=... \
  scripts/media/publicar_r2.sh            # --dry-run lista o que iria, sem enviar
```

O nome de cada arquivo leva o hash do conteúdo: o script só envia o que ainda não está no bucket, nunca
apaga (um catálogo já semeado pode usar as antigas) e põe `Cache-Control: public, max-age=31536000,
immutable` e `Content-Type: image/webp` em cada uma. Conferir:

```bash
curl -sI https://media.<domínio>/produtos/<arquivo>.webp | grep -iE 'cache-control|content-type|cf-cache-status'
# na segunda vez, cf-cache-status: HIT
```

As fotos são CC0 ou domínio público; os créditos ficam em
[`infra/media/CREDITOS.md`](../infra/media/CREDITOS.md), linkado no README do projeto.

## Deploy pelo CI

O [`deploy.yml`](../.github/workflows/deploy.yml) roda ao fim de cada pipeline de imagem na `main`
(os serviços, o backup e as imagens da stack), mas publica **uma vez por commit**: só o disparo do último
pipeline a terminar segue, e só com todos verdes ([`coolify/vez-de-publicar.sh`](coolify/vez-de-publicar.sh)).
Antes, cada pipeline publicava, e cada publicação recria os contêineres: o merge da release #360 derrubou a API
três vezes. Com um pipeline vermelho, nada do commit é publicado, e o job fica vermelho. A publicação chama o
[`coolify/deploy.sh`](coolify/deploy.sh):

1. a `:main` de cada imagem ganha a tag fixa deste deploy, `deploy-<id da execução>`;
2. o `IMAGE_TAG` e o `SIM_IMAGE_TAG` da stack no Coolify passam a ser essa tag;
3. o deploy é pedido pela API, e o job espera o desfecho (`finished`, ou o log do erro);
4. de fora, pelo domínio, cada serviço responde.

No recurso do Coolify: **Branch** `main` e **Auto Deploy** desligado.

No GitHub:

| Tipo | Nome | Valor |
|---|---|---|
| Secret do ambiente `producao` | `COOLIFY_TOKEN` | token da API do Coolify (**Keys & Tokens → API tokens**, com permissão de deploy e de escrita) |
| Variable do repositório | `COOLIFY_URL` | `https://coolify.digitalcodexa.com` |
| Variable do repositório | `URL_API` | `https://api.<domínio>`, sem barra no fim |
| Variable do repositório | `COOLIFY_APP_UUID` | o uuid do recurso, que aparece na URL dele no painel. É ela que liga o deploy: crie por último |

O segredo fica em **Settings → Environments → producao**, ambiente que só aceita a `main`. As variáveis ficam
no repositório (**Settings → Secrets and variables → Actions**), porque o `if` do job não enxerga as de ambiente.

Enquanto `COOLIFY_APP_UUID` não existir, o workflow não roda. Para publicar sem merge (depois de mudar
uma variável no painel, por exemplo): **Actions → deploy → Run workflow**.

Depois do deploy, o passo **Responde de fora** pede a API pelo domínio. Ele confere só os serviços do compose que o
recurso usa (o **Docker Compose Location** dele, lido por [`coolify/servicos.sh`](coolify/servicos.sh)): o fulfillment
entra na checagem da demonstração, e não na da produção. Trocar de compose não pede mudança no workflow, mas a lista
`IMAGES` do `deploy.yml` precisa cobrir as imagens dos dois.

A API vai antes da loja: quando um commit dispara um pipeline de imagem, o job da loja espera o pipeline e o deploy
da API terminarem ([`esperar-api.sh`](esperar-api.sh)). Se um deles falhar, a loja não é publicada e continua na
versão anterior: corrija ou reexecute o que falhou e depois reexecute o `web`. Um commit só de front publica direto.

O deploy de um compose recria os contêineres que mudaram: a API fica alguns segundos fora durante a troca
(o Coolify só faz rolling update de aplicação de um contêiner). O BFF da loja trata a API fora como
qualquer falha de rede.

**Voltar uma versão:** no painel, troque o `IMAGE_TAG` para a tag de um deploy anterior (o resumo de
cada execução do workflow diz qual subiu, e as tags `deploy-*` ficam no GHCR) e faça o deploy pelo
painel. O próximo merge na `main` publica de novo a partir da `:main`.

## Aviso de manutenção

Durante o deploy da API, a loja avisa "Estamos atualizando a loja", troca o erro do catálogo pelo aviso (a página
tenta de novo sozinha) e segura o "Fazer o pedido" com a explicação (#370). A troca dos contêineres deixa a API
fora por cerca de 40 s (#310). A marca fica no **Cloudflare KV**, e não na API, porque a API é o que sai do ar:

- o `deploy.yml` liga a marca antes de publicar ([`coolify/aviso-manutencao.sh`](coolify/aviso-manutencao.sh)) e a
  desliga ao terminar, mesmo se o deploy falhar. Ela tem prazo de 15 min, para sumir sozinha se o job morrer;
- o KV leva alguns segundos para valer em todo lugar, e a loja guarda a leitura por até 30 s: o aviso pode aparecer
  um pouco depois do começo da troca e ficar um pouco depois da volta;
- sem as variáveis, ou com o Cloudflare fora, o deploy segue normalmente, e a loja mostra o erro de antes.

O painel tem **Manutenção** (`/admin/manutencao`), onde a equipe agenda uma janela (até 24 h, com até 90 dias de
antecedência) e um aviso opcional. A loja mostra a faixa antes e durante a janela, e ela some sozinha no fim.

**Para ligar (uma vez, na conta da Cloudflare):**

1. Crie o namespace: **Workers & Pages → KV → Create**, com o nome `compra-facil-manutencao` (ou
   `npx wrangler kv namespace create compra-facil-manutencao`). Anote o **id**.
2. Ponha o id no `binding` `MAINTENANCE` do [`wrangler.jsonc`](../frontend/web/wrangler.jsonc), no lugar dos zeros.
   O job de deploy da loja recusa publicar com os zeros.
3. No GitHub, **Settings → Secrets and variables → Actions → Variables**: `CLOUDFLARE_KV_MAINTENANCE_ID` com o mesmo id.
4. No token do secret `CLOUDFLARE_API_TOKEN` (**My Profile → API Tokens**), acrescente a permissão
   **Account → Workers KV Storage → Edit**. O Worker lê o KV pelo binding e não precisa do token.

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

- **Alertas**: chegam por e-mail ([`grafana/README.md`](grafana/README.md)), com o que olhar em cada um.
- **Logs**: no Grafana Cloud (Explore → Loki, por `service_name`) ou na aba Logs de cada serviço no Coolify.
- **Um serviço não sobe**: o healthcheck dele reprova, e o Coolify mostra o contêiner como *unhealthy*.
  Faltar variável aparece no começo do deploy.
- **Rate limit pegando todo mundo junto**: o DNS da API foi para o proxy da Cloudflare (laranja). Volte
  para *DNS only*.
