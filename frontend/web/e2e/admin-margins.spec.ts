import { expect, test } from "@playwright/test";

test("mostra a margem por pedido, sem inventar a que não dá para calcular", async ({ page }) => {
  await page.goto("/admin/margens");
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/admin\/margens$/);

  // a soma só conta os dois pedidos com a conta completa: 34,02 - 10,49
  await expect(page.getByText(/em 2 pedidos com a conta completa/)).toContainText("R$ 23,53");

  const row = (number: string) => page.getByRole("row", { name: new RegExp(`^${number}`) });
  await expect(row("A1000000")).toContainText("R$ 34,02");
  await expect(row("A2000000")).toContainText("-R$ 10,49");
  await expect(row("A3000000")).toContainText("Sem custo");
  await expect(row("A4000000")).toContainText("Estornado");
});
