# ADR 0006 — Produção: API no Coolify da Hetzner, loja no Cloudflare Workers

- **Status:** aceito
- **Data:** 2026-10-27
- **Issues:** #157, #159, #160, #161, #162, #163

## Contexto

O plano da F9 era uma VM ARM na Oracle (Always Free) criada por OpenTofu, com k3s, Argo CD (GitOps),
CloudNativePG com backup no Object Storage, e a loja na Vercel. A infraestrutura chegou a ser escrita e
validada num k3d (PRs #196, #197 e #198), mas nunca foi aplicada.

Os outros projetos do Mateus já rodam de outro jeito: as APIs no **Coolify de um VPS da Hetzner** (ARM64,
8 GB, já pago), com deploy pedido pelo CI e backup diário do Postgres num S3, e os fronts Next.js no
**Cloudflare Workers** pelo OpenNext. Manter um segundo modelo de produção só para este projeto significa
outra conta, outro painel e outra rotina de backup e de atualização para uma pessoa só cuidar.

## Decisão

A produção segue o padrão dos outros projetos:

- **API no Coolify**, no VPS da Hetzner: um recurso Docker Compose com o gateway (Traefik, as mesmas
  regras do compose local), os seis serviços, o Redpanda e o Redis, e o Postgres como banco do próprio
  Coolify, com um banco por serviço e o backup diário no S3. As imagens são as do **GHCR** (multi-arch,
  tag `sha-<commit>`), já geradas pelo CI: o VPS não compila nada. Os pacotes ficam públicos, como o código.
- **Deploy pelo CI**: o auto-deploy do Coolify fica desligado; depois dos testes, o CI pede o deploy pela
  API do Coolify, espera o desfecho e confere de fora que a produção responde com o commit do push.
- **Loja no Cloudflare Workers** pelo `@opennextjs/cloudflare`, publicada pelo CI com o Wrangler. O IP do
  comprador, que o BFF repassa ao gateway para o rate limit (#176), vem do `CF-Connecting-IP`.
- **Fotos dos produtos no Cloudflare R2**, com domínio próprio e a CDN da Cloudflare.
- **Observabilidade no Grafana Cloud** (plano gratuito): o OpenTelemetry Collector no VPS exporta traces,
  métricas e logs para lá, e os alertas saem por e-mail. A stack LGTM continua no compose local.

O código da Oracle e do k3s de produção (`infra/oci`, `infra/k8s/producao`, `scripts/deploy`) sai do
repositório. Ele está no commit `13e5dfb6`, se um dia for útil. O chart do k3d local (`infra/k8s/helm`),
que mostra o HPA do catálogo, continua.

## Consequências

- **Sem custo novo e um modelo só**: o VPS já é pago, e o deploy, o backup e o painel são os mesmos dos
  outros projetos.
- **Memória dividida**: o VPS é de 8 GB e roda outros projetos. Cada contêiner tem limite de memória, a
  stack de observabilidade fica fora (Grafana Cloud) e o Redpanda roda com memória fixa.
- **Um servidor, sem escala horizontal na produção**: uma réplica de cada serviço. O HPA continua
  demonstrado no k3d (`task k8s:up`), não na produção. O que protege os dados é o backup diário do
  Postgres, e não uma réplica.
- **Menos GitOps**: o estado do servidor fica no painel do Coolify (variáveis, domínio, limites), não no
  git. O compose de produção e o runbook de deploy ficam versionados.
- **Backup diário em vez de PITR**: o backup do Coolify é um dump por dia. Uma perda volta para o último
  dump, e não para um instante qualquer, como faria o WAL contínuo do CloudNativePG.
- **`middleware.ts` em vez de `proxy.ts` no front**: o OpenNext só suporta o middleware no runtime Edge,
  então a renovação da sessão volta para a convenção que o Next 16 deprecou (o `proxy.ts` roda em Node).
  O CI roda o e2e inteiro contra o Worker, para o que funciona no Next e não no Workers aparecer antes da
  produção. Quando o OpenNext suportar o `proxy.ts`, o arquivo volta.
- **Dependência de uma pessoa**: o VPS e as contas da Cloudflare são do Mateus.
