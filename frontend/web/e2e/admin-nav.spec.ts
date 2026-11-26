import { expect, test, type Page } from "@playwright/test";
import { passTwoFactor } from "./two-factor";

/**
 * O menu do painel (#382): em telas grandes, uma coluna com as seções em grupos; no celular, um botão com a
 * seção atual que abre a mesma lista. Um só menu na página.
 */
async function signIn(page: Page, path: string) {
  await page.goto(path);
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await passTwoFactor(page);
  await expect(page).not.toHaveURL(/\/entrar/);
}

test("telas grandes: a coluna mostra os grupos e marca a seção atual", async ({ page }) => {
  await signIn(page, "/admin/campanhas");
  const nav = page.getByRole("navigation", { name: "Seções do painel" });
  await expect(nav).toBeVisible();
  for (const group of ["Vendas", "Catálogo", "Operação", "Sistema"]) {
    await expect(nav.getByRole("list", { name: group })).toBeVisible();
  }
  await expect(nav.getByRole("link", { name: "Campanhas" })).toHaveAttribute("aria-current", "page");
  await expect(page.getByRole("button", { name: /menu do painel/ })).toBeHidden();
  await expect(page.getByRole("navigation", { name: "Seções do painel" })).toHaveCount(1);
});

test("celular: o botão abre o menu, o Esc fecha e trocar de seção também", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await signIn(page, "/admin/campanhas");
  const toggle = page.getByRole("button", { name: /menu do painel/ });
  const nav = page.getByRole("navigation", { name: "Seções do painel" });
  await expect(toggle).toHaveText(/Campanhas/); // a seção atual no lugar de "Menu"
  await expect(nav).toBeHidden();

  await toggle.click();
  await expect(toggle).toHaveAttribute("aria-expanded", "true");
  await nav.getByRole("link", { name: "Produtos" }).focus();
  await page.keyboard.press("Escape");
  await expect(nav).toBeHidden();
  await expect(toggle).toBeFocused();

  await toggle.click();
  await nav.getByRole("link", { name: "Categorias" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Categorias");
  await expect(nav).toBeHidden();
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBe(0);
});

test("o manual fica no painel, com o caminho do pedido ligado às telas (#483)", async ({ page }) => {
  await signIn(page, "/admin");
  await page
    .getByRole("navigation", { name: "Seções do painel" })
    .getByRole("link", { name: "Manual" })
    .click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Manual do painel");
  // o índice e as partes vêm da mesma lista
  await page
    .getByRole("navigation", { name: "Nesta página" })
    .getByRole("link", { name: "Pôr um produto à venda" })
    .click();
  await expect(page).toHaveURL(/\/admin\/manual#a-venda$/);
  // a tabela das telas sai do próprio menu
  await expect(page.getByRole("region", { name: "O que cada tela faz" })).toContainText(
    "O dia a dia: a fila, o pedido feito, o rastreio, a entrega e as correções.",
  );
  await page.screenshot({ path: "test-results/manual-desktop.png", fullPage: true });

  // no celular, o índice abre no topo da página
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/admin/manual");
  await page.locator("summary", { hasText: "Nesta página" }).click();
  await page.screenshot({ path: "test-results/manual-celular.png", fullPage: true });
  const scrolls = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  expect(scrolls, "nada passa da largura da tela").toBe(false);
  await page.setViewportSize({ width: 1280, height: 800 });

  // cada etapa feita numa tela leva até ela
  await page.getByRole("link", { name: "Abrir Pedidos a fazer" }).first().click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Pedidos a fazer");
});
