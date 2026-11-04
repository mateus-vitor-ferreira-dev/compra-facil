import { expect, test, type Page } from "@playwright/test";

/**
 * O menu do painel (#382): em telas grandes, uma coluna com as seções em grupos; no celular, um botão com a
 * seção atual que abre a mesma lista. Um só menu na página.
 */
async function signIn(page: Page, path: string) {
  await page.goto(path);
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
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
