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

### Vender sem nota fiscal (MEI)

O MEI não é obrigado a emitir nota nas vendas a pessoa física, salvo se o cliente pedir (confirmar com o
contador, #226). Enquanto não houver certificado A1, o fulfillment roda com `Invoice__Enabled=false`: o
pedido pago vai direto ao fornecedor, com o endereço e sem chave nem XML, e nada é emitido nem cancelado.
A nota pedida pelo cliente sai à mão, pelo meio que o contador indicar. Quando o A1 e a Nuvem Fiscal
estiverem prontos (#229), basta tirar a variável: os pedidos novos passam a sair com nota.

Para emitir pela Nuvem Fiscal, além das credenciais (`Invoice__ClientId`, `Invoice__ClientSecret`, com os
escopos `nfe cep`), o emitente (`Invoice__Issuer__*`) e os códigos fiscais que o contador confirma:

| Variável | Padrão | O que é |
|---|---|---|
| `Invoice__Issuer__TaxRegime` | `1` | CRT. O MEI é `4` (NT 2024.001), que só aceita alguns CFOPs |
| `Invoice__CfopInState`, `Invoice__CfopOutOfState` | `5120`, `6120` | CFOP da venda (venda à ordem). Com CRT 4, a SEFAZ pede `5102`/`6102` (rejeição 337) |
| `Invoice__Csosn`, `Invoice__ReturnCsosn` | `102`, `900` | CSOSN da venda e da devolução (rejeição 782 sem o 900 na devolução) |
| `Invoice__PisCofinsCst`, `Invoice__ReturnPisCofinsCst` | `49`, `99` | CST do PIS e da COFINS, sem destaque |
| `Invoice__ProductOrigin` | `0` | Origem da mercadoria; `2` para importado comprado no Brasil |
| `Invoice__Environment` | `homologacao` | Na homologação, o destinatário sai com o nome que a SEFAZ exige; `producao` para valer |

### Fornecedor manual (#412)

Enquanto não houver fornecedor com API, o fulfillment roda com `Supplier__Mode=manual`: a equipe sobe a planilha do
fornecedor no painel e faz cada pedido no site dele. Nada muda para o cliente: o checkout, o frete, o pagamento e o
estorno são os mesmos.

- **Planilha** (`POST /fulfillment/v1/admin/supplier-catalog/sheet`, e o modelo em `.../sheet/template`): CSV com
  `sku;nome;categoria;custo;estoque`, e as opcionais `peso_g` e `foto` (https). A `categoria` é o slug da categoria da
  loja, e o `custo` é em reais ("12,90"). Uma linha com erro e nada muda; sem erro, a planilha nova substitui a anterior,
  e o que entrou, mudou ou saiu chega à loja pela sincronização de sempre. O preço sai das margens de `/admin/fornecedor`.
- **Estoque:** o da planilha, menos `Supplier__Manual__SafetyStock` (padrão 1), porque ela envelhece entre uma subida e outra.
- **Frete:** a tabela da loja em reais, até haver a do fornecedor escolhido: `Supplier__Manual__Freight__0__Service`
  (`manual-economico` ou `manual-expresso`), `__BaseCents` (até 1 kg), `__PerExtraKgCents` (cada quilo começado depois),
  `__MinDays` e `__MaxDays`; o acréscimo por região do CEP (`Supplier__Manual__RegionPercent__<1º dígito>`) e os dias a
  mais (`RegionExtraDays`); `Supplier__Manual__HandlingDays` (padrão 2) é o prazo do fornecedor para despachar.
- **Pedido pago:** fica `awaiting_staff`, na fila "Pedidos a fazer" (`GET .../supplier-orders/to-place`, com o endereço e
  o CPF do cliente para o operador). A rotina da equipe, pelo painel:
  1. faz o pedido no site do fornecedor e registra o número dele e o que pagou (`.../placed`): o cliente vê "enviado ao
     fornecedor", e a margem passa a usar o valor pago;
  2. se o fornecedor não tiver o produto, marca "sem estoque" (`.../out-of-stock`): o pedido falha e o cliente é estornado;
  3. informa o rastreio quando o fornecedor despachar (`.../tracking`): o cliente recebe o código;
  4. marca a entrega (`.../delivered`) quando o rastreio mostrar.
- **Cancelar:** na fila, o cancelamento do painel estorna o cliente na hora. Depois de feito no fornecedor, só com a
  confirmação de que a equipe cancelou lá (`cancelled_at_supplier`), para a loja não pagar um pedido já estornado.
- **Devolução:** o fornecedor manual não abre disputa; a devolução cai como recusada na abertura, e a equipe combina com o
  fornecedor e instrui o cliente pela tela de devoluções, como já faz hoje.

### Receber pela Efí (#379)

O payments cobra pela Efí: o cartão na API Cobranças, o PIX na API Pix. No painel da Efí (**API → Aplicações**),
crie uma aplicação com os escopos de Cobranças e de Pix e gere o certificado `.p12` de produção (não é o A1 da
nota). No Coolify:

| Variável | Valor |
|---|---|
| `EFI_CLIENT_ID`, `EFI_CLIENT_SECRET` | as credenciais da aplicação |
| `EFI_PIX_CERTIFICATE_BASE64` | o `.p12` em base64 (`base64 -w0 certificado.p12`), exigido em toda chamada da API Pix |
| `EFI_PIX_KEY` | a chave PIX da conta que recebe |
| `EFI_PIX_WEBHOOK_SECRET` | um segredo aleatório (`openssl rand -hex 24`): vai na URL do webhook do PIX e é conferido em cada aviso |
| `EFI_CHARGE_NOTIFICATION_URL` | `https://<API>/payments/v1/webhooks/efi/charges` |
| `EFI_PIX_WEBHOOK_URL` | `https://<API>/payments/v1/webhooks/efi/pix` |
| `EFI_STORE_NAME`, `EFI_PROJECT_TAG` | o nome que o cliente vê no banco (padrão `Embrulhado`) e o projeto no `custom_id` do cartão (padrão `embrulhado`), que separa esta loja no extrato |

O identificador de conta (32 caracteres) vai no Worker da loja, em `PAYMENT_PAYEE_CODE`, com
`PAYMENT_PROVIDER=production`.

**Homologação na máquina local**, antes de ligar a venda: as credenciais e o `.p12` de homologação num
`~/.efi/homologacao.env` (fora do repositório, chmod 600, com os nomes da tabela acima, mais `EFI_P12` com o caminho
do certificado e `PAYMENT_PAYEE_CODE`). O `task efi:homologacao` sobe o ambiente com a Efí no lugar do payment-sim
(`infra/compose/compose.efi.yaml`) e um túnel do Cloudflare para os avisos dela chegarem, e mostra o comando do front
com `PAYMENT_PROVIDER=sandbox`. Só a homologação: com a produção, o payments local cadastraria o webhook do PIX da
conta de verdade apontando para esta máquina. O `scripts/efi-conferir.sh homologacao|producao` pede o token nas duas
APIs e mostra só o status e os escopos, e o `scripts/efi-cofre.sh guardar|restaurar` leva o `~/.efi` para o
Bitwarden (uma nota segura por ambiente, com o `.p12` em base64) e de volta, em outro computador.

Para testar a homologação no servidor: `EFI_CHARGES_URL=https://cobrancas-h.api.efipay.com.br/`,
`EFI_PIX_URL=https://pix-h.api.efipay.com.br/` e as credenciais e o certificado de homologação. Lá, o último
dígito do cartão decide (1, 2 e 3 recusam) e o PIX de até R$ 10,00 é pago sozinho.

O payments cadastra o webhook do PIX ao subir, sem a validação de mTLS da Efí (`x-skip-mtls-checking`): a proteção
é o segredo na URL e a consulta à Efí, que é a verdade de cada aviso. O navegador tokeniza o cartão com a
biblioteca da Efí e o identificador de conta (vai no Worker da loja).

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
