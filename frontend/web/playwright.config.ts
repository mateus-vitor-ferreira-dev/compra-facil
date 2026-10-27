import { defineConfig, devices } from "@playwright/test";

const STUB = "http://localhost:4010";

/**
 * Testes de navegador contra o build de produção (`next start`), com a API falsa (e2e/stub-api.mjs)
 * no lugar do gateway: rápidos e sem a stack inteira. O caminho com os serviços de verdade é o e2e da
 * compra (load-tests/k6/e2e-compra.js).
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
    {
      // o cache de dados do Next fica em disco: sem limpar, o preço de uma execução vaza para a próxima
      command:
        "node -e \"require('fs').rmSync('.next/cache/fetch-cache',{recursive:true,force:true})\" && npm run start",
      url: "http://localhost:3000/robots.txt",
      env: { API_URL: STUB },
      reuseExistingServer: !process.env.CI,
    },
  ],
});
