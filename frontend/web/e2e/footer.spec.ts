import { expect, test } from "@playwright/test";

// a loja de verdade (sem DEMO_MODE) não pode dizer ao cliente que nada é cobrado
test("o rodapé da loja de verdade descreve a loja, sem o aviso de demonstração", async ({ page }) => {
  await page.goto("/");
  const footer = page.getByRole("contentinfo");
  await expect(footer).toContainText("Eletrônicos, informática, casa e esporte");
  await expect(footer).not.toContainText("demonstração");
  await expect(page.getByText("Nada é cobrado")).toHaveCount(0);
});
