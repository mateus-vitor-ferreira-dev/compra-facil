import { expect, test } from "@playwright/test";

test("mostra as mensagens com falha por serviço e reprocessa", async ({ page }) => {
  await page.goto("/admin/falhas");
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/admin\/falhas$/);

  const inventory = page.getByRole("region", { name: /^Estoque/ });
  await expect(inventory).toHaveAccessibleName(/2 mensagens/);
  await expect(inventory.getByRole("listitem")).toHaveCount(2);
  await expect(inventory).toContainText("Mensagem que não é um evento válido");
  await expect(inventory).toContainText("OrderPlaced");
  await expect(inventory).toContainText("4 tentativas");
  // serviço com o Kafka fora: a seção explica, sem derrubar a página
  await expect(page.getByRole("region", { name: /^Pagamentos/ })).toContainText("O Kafka não respondeu");
  // serviço sem falhas não aparece
  await expect(page.getByRole("region", { name: /Pedidos/ })).toHaveCount(0);

  await inventory.getByRole("button", { name: "Reprocessar as 2" }).click();
  await expect(page.getByRole("status")).toHaveText(
    "2 mensagens devolvidas ao serviço de estoque. Se falharem de novo, voltam para cá.",
  );
  // envenenadas falham de novo e voltam: continuam à vista
  await expect(page.getByRole("region", { name: /^Estoque/ })).toBeVisible();
});
