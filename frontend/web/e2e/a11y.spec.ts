import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";

/**
 * Acessibilidade automatizada (#155): o axe em cada tela principal, nos dois temas, contra as regras
 * WCAG 2.2 A e AA. O axe não substitui o teste com leitor de tela, mas pega o que regride sem ninguém ver.
 */
const TV = "/produto/0199f6a0-0000-7000-8000-000000000000";

async function audit(page: Page, name: string) {
  // na navegação pelo cliente, o título troca logo depois da URL
  await expect(page).toHaveTitle(/CompraFácil/);
  const results = await new AxeBuilder({ page })
    .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
    .analyze();
  const violations = results.violations.map((v) => ({
    rule: v.id,
    impact: v.impact,
    nodes: v.nodes.map((n) => n.target.join(" ")).slice(0, 5),
  }));
  expect(violations, `violações em ${name}`).toEqual([]);
}

async function signIn(page: Page, email: string) {
  await page.goto("/entrar");
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).not.toHaveURL(/\/entrar/);
}

for (const scheme of ["light", "dark"] as const) {
  test.describe(`tema ${scheme === "light" ? "claro" : "escuro"}`, () => {
    test.use({ colorScheme: scheme });

    test("vitrine, produto, busca e entrada", async ({ page }) => {
      for (const path of [
        "/",
        "/ofertas",
        "/categoria/fones",
        "/categoria/fones?marca=JBL&ordem=menor-preco",
        "/busca?q=fone",
        TV,
        "/entrar",
        "/cadastro",
      ]) {
        await page.goto(path);
        await audit(page, path);
      }
    });

    test("carrinho, checkout e o pedido ao vivo", async ({ page }) => {
      await signIn(page, "ana@exemplo.com");
      await page.goto(TV);
      await page.getByRole("button", { name: "Adicionar ao carrinho" }).click();
      await expect(page.getByRole("status").filter({ hasText: "Adicionado" })).toBeVisible();
      for (const path of ["/carrinho", "/checkout", "/conta"]) {
        await page.goto(path);
        await audit(page, path);
      }
      await page.goto("/checkout");
      // o cartão com os erros nos campos
      await page.getByRole("radio", { name: /Cartão de crédito/ }).check();
      await page.getByRole("button", { name: /^Fazer o pedido/ }).click();
      await expect(page.getByLabel("Número do cartão")).toBeFocused();
      await audit(page, "checkout no cartão");
      // o PIX: o painel com o QR e o copia e cola, e o pedido pago
      await page.getByRole("radio", { name: /PIX/ }).check();
      await page.getByRole("button", { name: /^Fazer o pedido/ }).click();
      await expect(page).toHaveURL(/\/conta\/pedidos\//);
      await audit(page, "pedido ao vivo");
      await expect(page.getByRole("region", { name: "Valor do PIX" })).toBeVisible({ timeout: 10_000 });
      await audit(page, "PIX a pagar");
      const id = new URL(page.url()).pathname.split("/").pop();
      await page.request.post(`http://localhost:4010/__pix/${id}/pay`);
      await expect(page.getByRole("status").first()).toContainText("Pagamento aprovado", { timeout: 10_000 });
      await audit(page, "pedido confirmado");
    });

    test("painel", async ({ page }) => {
      await signIn(page, "admin@exemplo.com");
      for (const path of [
        "/admin/produtos",
        "/admin/produtos/0199f6a0-0000-7000-8000-000000000001",
        "/admin/produtos/novo",
        "/admin/categorias",
        "/admin/campanhas",
        "/admin/campanhas/nova",
        "/admin/falhas",
        "/admin/atrasos",
        "/admin/fornecedor",
        "/admin/produtos/0199f6a0-0000-7000-8000-000000000063",
      ]) {
        await page.goto(path);
        await audit(page, path);
      }
    });
  });
}
