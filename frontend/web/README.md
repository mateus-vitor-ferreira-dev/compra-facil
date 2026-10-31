# Front da CompraFácil (Next.js)

Loja e BFF num app só: o navegador fala com o servidor do Next, e o servidor fala com as APIs pelo
gateway (`API_URL`, padrão `http://localhost`). Nenhum token chega ao JavaScript do navegador.

## Rodar

```bash
nvm use                      # Node 24 (.nvmrc)
npm ci
docker compose up -d --wait  # na raiz: a API no gateway (http://localhost)
npm run dev                  # http://localhost:3000
```

## Como está montado

|                             |                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Tipos da API**            | `src/lib/api/schema/*.ts`, gerados de `contracts/openapi/*.v1.yaml` (openapi-typescript). Mudou um contrato: `npm run api:types`. O CI falha se estiverem desatualizados (`npm run api:check`).                                                                                                                                                                                                                                                                                                                                                                     |
| **Cliente**                 | `src/lib/api/client.ts`: um cliente tipado (openapi-fetch) por serviço, só no servidor (`server-only`).                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
| **Sessão**                  | `src/lib/auth/session.ts`: access token (15 min) e refresh token (30 dias) em cookies `httpOnly`, `SameSite=Lax`, `Secure` em produção. Nunca no `localStorage`.                                                                                                                                                                                                                                                                                                                                                                                                    |
| **Login, cadastro, logout** | `src/app/api/auth/*`: route handlers que chamam o identity e gravam ou apagam os cookies. Só aceitam `application/json` do próprio site (CSRF).                                                                                                                                                                                                                                                                                                                                                                                                                     |
| **Google**                  | `src/lib/auth/google.ts` e `src/app/api/auth/google/*`: authorization code com PKCE, `state` e `nonce` num cookie httpOnly de 10 min; o callback troca o código pelo ID token e o entrega ao identity. E-mail que já tem conta com senha passa por `/entrar/vincular`. Com `GOOGLE_CLIENT_ID` e `GOOGLE_CLIENT_SECRET` (o redirect URI é `SITE_URL/api/auth/google/callback`); sem eles, o botão não aparece.                                                                                                                                                       |
| **Refresh**                 | `src/middleware.ts`: sem access token e com refresh token, renova antes da página. É `middleware.ts` (Edge), e não `proxy.ts` (Node): no Cloudflare Workers o OpenNext só suporta o primeiro. Um refresh por vez para cada token nesta instância; entre instâncias, o intervalo de reuso do identity (10 s) mantém a sessão.                                                                                                                                                                                                                                        |
| **Pedido**                  | `src/app/(loja)/checkout`: server action com `Idempotency-Key` gerada com a página (o reenvio devolve o mesmo pedido); 409 volta ao carrinho, 503 diz quando tentar e mostra o `trace_id`. `src/app/api/pedidos/[id]/eventos`: repassa o SSE do orders com o token do cookie (o `EventSource` não manda `Authorization`).                                                                                                                                                                                                                                           |
| **IP do comprador**         | `src/lib/api/gateway-headers.ts`: com `BFF_GATEWAY_KEY` (o mesmo do gateway) e `CLIENT_IP_HEADER` (o cabeçalho que a plataforma garante, `cf-connecting-ip` na Cloudflare), o BFF manda `X-Client-IP` e `X-Bff-Key`, e o gateway limita por comprador e não pela loja somada. Sem os dois, não manda nada.                                                                                                                                                                                                                                                          |
| **Painel**                  | `src/app/admin`: só papel `admin` (`requireAdmin`; sem o papel, 404). Produtos (busca, criar, editar com `If-Match`), estoque com motivo (`Idempotency-Key`) e categorias. As escritas invalidam o cache de dados da vitrine.                                                                                                                                                                                                                                                                                                                                       |
| **Observabilidade**         | `src/instrumentation.ts` (OpenTelemetry com `@vercel/otel`, quando há `OTEL_EXPORTER_OTLP_ENDPOINT`; no Workers ele não carrega, e quem mede é a observabilidade da Cloudflare): páginas, server actions e rotas do BFF viram spans, e as chamadas ao gateway levam o `traceparent`, então um pedido aparece num trace só, do Next ao e-mail. Os Web Vitals do navegador vão por `sendBeacon` a `/api/vitals` e viram histogramas (dashboard **Loja (front)** no Grafana). Exporta quando há `OTEL_EXPORTER_OTLP_ENDPOINT` (`http://localhost:4318` com o compose). |
| **UI**                      | Tailwind 4 e shadcn/ui (`components.json`). A identidade visual vem no design system (#149).                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |

## Testes

```bash
npm test         # Vitest + Testing Library
npm run e2e      # Playwright com a API falsa, incluindo o axe (WCAG 2.2 AA) nos dois temas
npm run build
npm run e2e      # Playwright contra o build, com a API falsa (e2e/stub-api.mjs)
npm run lhci     # Lighthouse CI: performance, acessibilidade, SEO e boas práticas ≥ 90 (celular)
npx opennextjs-cloudflare build
E2E_WORKER=1 npm run e2e   # os mesmos testes contra o Worker da produção, no workerd
```

O caminho com os serviços de verdade é o e2e da compra (`task e2e`).

## Produção (Cloudflare Workers)

A loja roda no Cloudflare Workers pelo OpenNext ([ADR 0006](../../docs/adr/0006-producao-no-coolify-e-cloudflare.md)):
`wrangler.jsonc` e `open-next.config.ts`. O job `deploy` do `web.yml` publica a partir da `main`, depois dos
testes (incluindo o e2e contra o Worker), e confere de fora que a loja responde.

Uma vez, na Cloudflare:

1. **Workers & Pages → compra-facil-web → Settings → Domains & Routes**: o domínio da loja como Custom
   Domain (a zona já na Cloudflare). Antes do primeiro deploy do CI o Worker não existe: rode `npm run
deploy` uma vez da máquina (`npx wrangler login` antes) ou deixe o CI criar e ligue o domínio depois.
2. Em `wrangler.jsonc`, `API_URL` e `SITE_URL` com os domínios de verdade.
3. Os segredos do Worker:

   ```bash
   npx wrangler secret put BFF_GATEWAY_KEY        # o mesmo valor do gateway (deploy/README.md)
   npx wrangler secret put GOOGLE_CLIENT_SECRET   # e GOOGLE_CLIENT_ID em vars, se houver login com o Google
   ```

4. **Workers & Pages → Observability**: os destinos OTLP para o Grafana Cloud (traces e logs do Worker),
   como em [`deploy/grafana/README.md`](../../deploy/grafana/README.md#a-loja-cloudflare-workers).

No GitHub (**Settings → Secrets and variables → Actions**):

| Tipo     | Nome                    | Valor                                        |
| -------- | ----------------------- | -------------------------------------------- |
| Secret   | `CLOUDFLARE_API_TOKEN`  | token com o modelo _Edit Cloudflare Workers_ |
| Variable | `CLOUDFLARE_ACCOUNT_ID` | o id da conta (lateral do painel)            |
| Variable | `URL_LOJA`              | `https://<domínio>`, sem barra no fim        |

Enquanto `CLOUDFLARE_ACCOUNT_ID` não existir, o deploy é pulado.

Localmente, `npm run preview` faz o build do Worker e o sobe no workerd (porta 8787), com os valores de
`.dev.vars` (fora do git; ex.: `API_URL=http://localhost`).

## Dados da empresa e políticas

As páginas em `/politicas` e o rodapé leem as variáveis abaixo, sem valor padrão: sem elas, a loja não mostra dados da empresa, e as políticas aparecem com os trechos a definir marcados.

| Variável            | Para quê                                                                               |
| ------------------- | -------------------------------------------------------------------------------------- |
| `LEGAL_NAME`        | Nome empresarial                                                                       |
| `LEGAL_CNPJ`        | CNPJ                                                                                   |
| `LEGAL_ADDRESS`     | Endereço exigido pelo Decreto 7.962/2013                                               |
| `LEGAL_EMAIL`       | Canal de atendimento e de dados pessoais                                               |
| `LEGAL_PHONE`       | Telefone (opcional)                                                                    |
| `POLICIES_REVIEWED` | `true` só depois da revisão do advogado: tira o aviso de rascunho e libera a indexação |

Mesmo com `POLICIES_REVIEWED=true`, o texto mantém os trechos a definir até serem editados.
