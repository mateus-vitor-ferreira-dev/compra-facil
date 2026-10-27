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

|                             |                                                                                                                                                                                                                                                                                                                           |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Tipos da API**            | `src/lib/api/schema/*.ts`, gerados de `contracts/openapi/*.v1.yaml` (openapi-typescript). Mudou um contrato: `npm run api:types`. O CI falha se estiverem desatualizados (`npm run api:check`).                                                                                                                           |
| **Cliente**                 | `src/lib/api/client.ts`: um cliente tipado (openapi-fetch) por serviço, só no servidor (`server-only`).                                                                                                                                                                                                                   |
| **Sessão**                  | `src/lib/auth/session.ts`: access token (15 min) e refresh token (30 dias) em cookies `httpOnly`, `SameSite=Lax`, `Secure` em produção. Nunca no `localStorage`.                                                                                                                                                          |
| **Login, cadastro, logout** | `src/app/api/auth/*`: route handlers que chamam o identity e gravam ou apagam os cookies. Só aceitam `application/json` do próprio site (CSRF).                                                                                                                                                                           |
| **Refresh**                 | `src/proxy.ts`: sem access token e com refresh token, renova antes da página. Um refresh por vez para cada token nesta instância; entre instâncias, o intervalo de reuso do identity (10 s) mantém a sessão.                                                                                                              |
| **Pedido**                  | `src/app/(loja)/checkout`: server action com `Idempotency-Key` gerada com a página (o reenvio devolve o mesmo pedido); 409 volta ao carrinho, 503 diz quando tentar e mostra o `trace_id`. `src/app/api/pedidos/[id]/eventos`: repassa o SSE do orders com o token do cookie (o `EventSource` não manda `Authorization`). |
| **UI**                      | Tailwind 4 e shadcn/ui (`components.json`). A identidade visual vem no design system (#149).                                                                                                                                                                                                                              |

## Testes

```bash
npm test         # Vitest + Testing Library
npm run build
npm run e2e      # Playwright contra o build, com a API falsa (e2e/stub-api.mjs)
npm run lhci     # Lighthouse CI: performance, acessibilidade, SEO e boas práticas ≥ 90 (celular)
```

O caminho com os serviços de verdade é o e2e da compra (`task e2e`).
