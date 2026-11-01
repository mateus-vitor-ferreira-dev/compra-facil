import { expect, test } from "@playwright/test";

test("mostra os pedidos atrasados no fornecedor por etapa", async ({ page }) => {
  await page.goto("/admin/atrasos");
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/admin\/atrasos$/);
  await expect(page.getByRole("link", { name: "Pedidos no fornecedor" })).toHaveAttribute(
    "aria-current",
    "page",
  );

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

test("cancela o pedido parado com confirmação e mostra os que não chegaram", async ({ page }) => {
  await page.goto("/admin/atrasos");
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();

  const failed = page.getByRole("region", { name: /^Não chegaram ao fornecedor/ });
  await expect(failed).toContainText("Pedido 3F6A9C10, Sem estoque no fornecedor");
  await expect(failed.getByRole("button")).toHaveCount(0); // não há reenvio: o cliente já foi estornado

  // a primeira tecla só pergunta; desistir devolve o foco ao botão
  const cancel = page.getByRole("button", { name: "Cancelar o pedido 5B8E1F20" });
  await cancel.click();
  await expect(page.getByText(/Cancelar o pedido 5B8E1F20\? O fornecedor é avisado/)).toBeFocused();
  await page.getByRole("button", { name: "Manter o pedido" }).click();
  await expect(cancel).toBeFocused();

  await cancel.click();
  await page.getByRole("button", { name: "Cancelar e estornar" }).click();
  await expect(page.getByRole("status")).toHaveText(
    "Pedido 5B8E1F20 cancelado. O estorno ao cliente já foi pedido.",
  );
});
