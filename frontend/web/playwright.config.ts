import { defineConfig, devices } from "@playwright/test";

const STUB = "http://localhost:4010";

// o servidor do front, contra a API falsa: o Google e o provedor de pagamento também são o stub
const FRONT_ENV = {
  API_URL: STUB,
  GOOGLE_CLIENT_ID: "loja-de-teste",
  GOOGLE_CLIENT_SECRET: "segredo-de-teste",
  GOOGLE_AUTH_URL: `${STUB}/google/authorize`,
  GOOGLE_TOKEN_URL: `${STUB}/google/token`,
  // o cartão é tokenizado pelo navegador direto no provedor: aqui, o stub
  PAYMENT_PROVIDER_URL: STUB,
  CEP_LOOKUP_URL: `${STUB}/cep`,
};

// E2E_WORKER=1: os mesmos testes contra o Worker da produção (o build do OpenNext no workerd, depois de
// `npx opennextjs-cloudflare build`), em vez do `next start` (ADR 0006). O --local-upstream mantém a origem
// local: sem ele, o wrangler usa o domínio de `routes`, e o redirect de uma Server Action, que o Next busca
// na própria origem, iria para a loja publicada, levando os cabeçalhos do teste
const front = process.env.E2E_WORKER
  ? {
      command: `npx wrangler dev --port 3000 --local-upstream localhost:3000 ${Object.entries({
        ...FRONT_ENV,
        SITE_URL: "http://localhost:3000",
      })
        .map(([k, v]) => `--var ${k}:${v}`)
        .join(" ")}`,
      url: "http://localhost:3000/robots.txt",
      reuseExistingServer: !process.env.CI,
    }
  : {
      // o cache de dados do Next fica em disco: sem limpar, o preço de uma execução vaza para a próxima
      command:
        "node -e \"require('fs').rmSync('.next/cache/fetch-cache',{recursive:true,force:true})\" && npm run start",
      url: "http://localhost:3000/robots.txt",
      env: FRONT_ENV,
      reuseExistingServer: !process.env.CI,
    };

/**
 * Testes de navegador contra o build de produção (`next start`, ou o Worker com E2E_WORKER), com a API
 * falsa (e2e/stub-api.mjs) no lugar do gateway: rápidos e sem a stack inteira. O caminho com os serviços
 * de verdade é o e2e da compra (load-tests/k6/e2e-compra.js).
 */
export default defineConfig({
  testDir: "e2e",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["github"], ["html", { open: "never" }]] : "list",
  use: { baseURL: "http://localhost:3000", trace: "retain-on-failure" },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: [
    { command: "node e2e/stub-api.mjs", url: `${STUB}/__stats`, reuseExistingServer: !process.env.CI },
    front,
  ],
});
