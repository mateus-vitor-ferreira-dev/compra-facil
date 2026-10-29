import { expect, test } from "@playwright/test";

test("mostra os pedidos atrasados no fornecedor por etapa", async ({ page }) => {
  await page.goto("/admin/atrasos");
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/admin\/atrasos$/);
  await expect(page.getByRole("link", { name: "Pedidos atrasados" })).toHaveAttribute("aria-current", "page");

  const shipping = page.getByRole("region", { name: /^O fornecedor não despachou/ });
  await expect(shipping).toHaveAccessibleName(/1 pedido$/);
  await expect(shipping).toContainText("Pedido 5B8E1F20, Aceito pelo fornecedor, sem envio");
  await expect(shipping).toContainText("Prazo venceu há 4 dias");
  await expect(shipping.getByRole("link", { name: "bia@exemplo.com" })).toHaveAttribute(
    "href",
    "mailto:bia@exemplo.com",
  );

  const delivery = page.getByRole("region", { name: /^O envio não chegou/ });
  await expect(delivery).toContainText("Prazo venceu há 5 horas");
  await expect(delivery.getByRole("link", { name: /Rastreio BR123456789CJ/ })).toHaveAttribute(
    "href",
    "https://t.17track.net/en#nums=BR123456789CJ",
  );
});
