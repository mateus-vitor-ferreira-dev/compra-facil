import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";

/**
 * A página que não existe: na loja, com o cabeçalho, o rodapé e as saídas de volta à compra (no lugar da 404
 * padrão do Next, que vinha em inglês); no painel, dentro do painel, com as seções.
 */
test("endereço que não existe: a 404 da loja, em português, com status 404", async ({ page }) => {
  const response = await page.goto("/uma-pagina-que-nao-existe");
  expect(response?.status()).toBe(404);
  await expect(page).toHaveTitle("Página não encontrada | CompraFácil");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Esta página não está na prateleira");
  await expect(page.getByText("This page could not be found")).toHaveCount(0);
  // a moldura da loja: a busca do cabeçalho e o rodapé
  await expect(page.getByRole("search").first()).toBeVisible();
  await expect(page.getByRole("contentinfo")).toBeVisible();
  await page.getByRole("link", { name: "Ver as ofertas" }).click();
  await expect(page).toHaveURL(/\/ofertas$/);
});

test("as categorias levam de volta ao catálogo", async ({ page }) => {
  await page.goto("/nao-existe");
  const categories = page.getByRole("navigation", { name: "Ou comece por uma categoria" });
  await expect(categories.getByRole("link").first()).toHaveAttribute("href", /^\/categoria\//);
});

test("produto que saiu do catálogo cai na mesma 404, já dentro da loja", async ({ page }) => {
  const response = await page.goto("/produto/0199f6a0-0000-7000-8000-0000000fffff");
  expect(response?.status()).toBe(404);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Esta página não está na prateleira");
});

test("no painel, a 404 fica dentro do painel, com as seções", async ({ page }) => {
  await page.goto("/entrar?voltar=%2Fadmin");
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/admin$/);
  const response = await page.goto("/admin/pedidos/01a18d66-0000-7000-8000-00000000dead");
  expect(response?.status()).toBe(404);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Isto não existe no painel");
  await expect(page.getByRole("navigation", { name: "Seções do painel" })).toBeVisible();
  await page.getByRole("link", { name: "Ver os pedidos" }).click();
  await expect(page).toHaveURL(/\/admin\/pedidos$/);
});

test("celular: sem rolagem para o lado", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/nao-existe");
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBe(0);
});

for (const scheme of ["light", "dark"] as const) {
  test(`404 sem violações do axe no tema ${scheme === "light" ? "claro" : "escuro"}`, async ({ page }) => {
    await page.emulateMedia({ colorScheme: scheme });
    await page.goto("/nao-existe");
    const results = await new AxeBuilder({ page })
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
      .analyze();
    expect(results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target).join(" ")}`)).toEqual([]);
  });
}
