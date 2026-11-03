import { expect, test, type Page } from "@playwright/test";

async function signIn(page: Page, path = "/admin/margens") {
  await page.goto(path);
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/admin\/margens/);
}

test("mostra a margem por pedido, sem inventar a que não dá para calcular", async ({ page }) => {
  await signIn(page);

  // a soma só conta os dois pedidos com a conta completa: 34,02 - 10,49
  await expect(page.getByText(/em 2 pedidos com a conta completa/)).toContainText("R$ 23,53");
  await expect(page.getByText(/^Fora da soma/)).toContainText("1 estornado e 1 pedido sem algum valor");

  const row = (number: string) => page.getByRole("row", { name: new RegExp(`^${number}`) });
  await expect(row("A1000000")).toContainText("R$ 34,02");
  await expect(row("A2000000")).toContainText("-R$ 10,49");
  await expect(row("A3000000")).toContainText("Sem custo");
  await expect(row("A4000000")).toContainText("Estornado");
});

test("o período filtra a lista e a soma, e funciona sem JavaScript (#282)", async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await signIn(page);

  await page.getByLabel("De", { exact: true }).fill("2026-10-20");
  await page.getByLabel("Até", { exact: true }).fill("2026-10-20");
  await page.getByRole("button", { name: "Ver o período" }).click();
  await expect(page).toHaveURL(/de=2026-10-20&ate=2026-10-20/);
  await expect(page.getByRole("row")).toHaveCount(3); // o cabeçalho e os 2 pedidos do dia
  await expect(page.getByText(/em 2 pedidos com a conta completa/)).toContainText("R$ 23,53");
  await expect(page.getByText(/^Fora da soma/)).toHaveCount(0);

  // só o devolvido: nada para somar, e ele aparece como fora da soma
  await page.goto("/admin/margens?de=2026-11-01&ate=2026-11-01");
  await expect(page.getByText("Nenhum pedido do período tem a conta completa para somar.")).toBeVisible();
  await expect(page.getByText(/^Fora da soma/)).toContainText("1 estornado");

  await page.goto("/admin/margens?de=2026-11-03&ate=2026-11-01");
  await expect(page.getByRole("alert")).toContainText("O início do período vem antes do fim.");
  await expect(page.getByRole("row")).toHaveCount(5); // mostra todos

  await page.getByRole("link", { name: "Todos os pedidos" }).click();
  await expect(page.getByRole("link", { name: "Todos os pedidos" })).toHaveAttribute("aria-current", "page");
  await context.close();
});
