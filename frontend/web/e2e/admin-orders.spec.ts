import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";

/**
 * Os pedidos do painel (#382): a lista com o contato de quem comprou, o filtro na URL e o detalhe. Em telas
 * grandes, a lista fica ao lado do pedido aberto; no celular, o pedido ocupa a tela, com a volta à lista.
 */
async function signIn(page: Page, path: string) {
  await page.goto(path);
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).not.toHaveURL(/\/entrar/);
}

const list = (page: Page) => page.getByRole("main").getByRole("list").first();

test("a lista mostra o contato, filtra pela URL e abre o pedido ao lado em tela grande", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await signIn(page, "/admin/pedidos");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Pedidos");
  const bia = list(page).getByRole("link", { name: /B1000001/ });
  await expect(bia).toContainText("Bia Lima");
  await expect(bia).toContainText("bia@exemplo.com");
  await expect(bia).toContainText("(35) 99999-0000"); // a lista sozinha tem espaço para o telefone
  await expect(bia).toContainText("Lavras/MG");
  await expect(bia).toContainText("Cartão em 3x");
  await expect(list(page).getByRole("link", { name: /B2000002/ })).toContainText("Cancelado pelo fornecedor");

  // o filtro fica na URL e a lista obedece
  await page.getByLabel("Buscar").fill("caio");
  await page.getByRole("button", { name: "Filtrar" }).click();
  await expect(page).toHaveURL(/q=caio/);
  await expect(list(page).getByRole("link")).toHaveCount(1);
  await page.getByRole("link", { name: "Limpar" }).click();
  await expect(list(page).getByRole("link")).toHaveCount(3);

  await list(page)
    .getByRole("link", { name: /B1000001/ })
    .click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Pedido B1000001");
  // a lista continua à mostra, com o pedido aberto marcado
  const side = page.getByRole("complementary", { name: "Lista de pedidos" });
  await expect(side).toBeVisible();
  await expect(side.getByRole("link", { name: /B1000001/ })).toHaveAttribute("aria-current", "page");
  await expect(page.getByRole("link", { name: "Conversar no WhatsApp" })).toHaveAttribute(
    "href",
    "https://wa.me/5535999990000",
  );
  await expect(page.getByText("Rastreio BR123")).toBeVisible();
  await expect(page.getByRole("link", { name: "Voltar aos pedidos" })).toBeHidden();

  // de um pedido a outro sem voltar
  await side.getByRole("link", { name: /B2000002/ }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Pedido B2000002");
  await expect(page.getByText("Valor estornado")).toBeVisible();
});

test("celular: o pedido ocupa a tela e volta à lista com o mesmo filtro", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await signIn(page, "/admin/pedidos?situacao=shipped");
  await list(page)
    .getByRole("link", { name: /B1000001/ })
    .click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Pedido B1000001");
  await expect(page.getByRole("complementary", { name: "Lista de pedidos" })).toBeHidden();
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBe(0);
  await page.getByRole("link", { name: "Voltar aos pedidos" }).click();
  await expect(page).toHaveURL(/\/admin\/pedidos\?situacao=shipped$/);
});

test("pedido que não existe dá 404, e o pedido sem nome nem destino ainda se explica", async ({ page }) => {
  await signIn(page, "/admin/pedidos/01a18d66-0000-7000-8000-0000b3000003");
  await expect(page.getByText("Sem nome no pedido")).toBeVisible();
  expect((await page.goto("/admin/pedidos/01a18d66-0000-7000-8000-00000000dead"))?.status()).toBe(404);
});

for (const scheme of ["light", "dark"] as const) {
  test(`lista e pedido sem violações do axe no tema ${scheme === "light" ? "claro" : "escuro"}`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme: scheme });
    await signIn(page, "/admin/pedidos");
    for (const path of ["/admin/pedidos", "/admin/pedidos/01a18d66-0000-7000-8000-0000b1000001"]) {
      await page.goto(path);
      const results = await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
        .analyze();
      expect(
        results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target).join(" ")}`),
        path,
      ).toEqual([]);
    }
  });
}
