import { expect, test } from "@playwright/test";

test("mostra as devoluções ao fornecedor, com as recusas primeiro", async ({ page }) => {
  await page.goto("/admin/devolucoes");
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/admin\/devolucoes$/);
  await expect(page.getByRole("link", { name: "Devoluções" })).toHaveAttribute("aria-current", "page");
  await expect(page.getByRole("heading", { level: 2 }).first()).toContainText("Recusadas pelo fornecedor");

  const rejected = page.getByRole("region", { name: /^Recusadas pelo fornecedor/ });
  await expect(rejected).toHaveAccessibleName(/1 devolução$/);
  await expect(rejected).toContainText("Pedido 6C3E4AB2, Recusada pelo fornecedor");
  await expect(rejected).toContainText("Motivo da recusa: produto fora da política");
  await expect(rejected).toContainText("A SEFAZ rejeitou a nota de devolução: 267: sem referência");
  await expect(rejected.getByRole("link", { name: "dani@exemplo.com" })).toHaveAttribute(
    "href",
    "mailto:dani@exemplo.com",
  );

  const overdue = page.getByRole("region", { name: /^Sem resposta além do prazo/ });
  await expect(overdue).toContainText("O fornecedor não respondeu (3 tentativas)");
  await expect(overdue).toContainText("Prazo venceu há 2 dias");
  await overdue.getByText("Último erro").click();
  await expect(overdue).toContainText("fornecedor respondeu 503");

  const waiting = page.getByRole("region", { name: /^Esperando o fornecedor/ });
  await expect(waiting).toContainText("Aberta no fornecedor");
  await expect(waiting).toContainText("código RMA00000002");
  await expect(waiting).not.toContainText("Prazo venceu");
});
