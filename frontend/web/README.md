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

| | |
|---|---|
| **Tipos da API** | `src/lib/api/schema/*.ts`, gerados de `contracts/openapi/*.v1.yaml` (openapi-typescript). Mudou um contrato: `npm run api:types`. O CI falha se estiverem desatualizados (`npm run api:check`). |
| **Cliente** | `src/lib/api/client.ts`: um cliente tipado (openapi-fetch) por serviço, só no servidor (`server-only`). |
| **Sessão** | `src/lib/auth/session.ts`: access token (15 min) e refresh token (30 dias) em cookies `httpOnly`, `SameSite=Lax`, `Secure` em produção. Nunca no `localStorage`. |
| **Login, cadastro, logout** | `src/app/api/auth/*`: route handlers que chamam o identity e gravam ou apagam os cookies. Só aceitam `application/json` do próprio site (CSRF). |
| **Refresh** | `src/proxy.ts`: sem access token e com refresh token, renova antes da página. Um refresh por vez para cada token nesta instância, porque o identity derruba a sessão se o mesmo refresh token for usado duas vezes. |
| **UI** | Tailwind 4 e shadcn/ui (`components.json`). A identidade visual vem no design system (#149). |

## Testes

```bash
npm test         # Vitest + Testing Library
npm run build
npm run e2e      # Playwright contra o build, com a API falsa (e2e/stub-api.mjs)
npm run lhci     # Lighthouse CI: performance, acessibilidade, SEO e boas práticas ≥ 90 (celular)
```

O caminho com os serviços de verdade é o e2e da compra (`task e2e`).
