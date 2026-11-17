import { expect, type Page } from "@playwright/test";

/** O código que o "app autenticador" da equipe mostra no stub (e2e/stub-api.mjs). */
export const ADMIN_TOTP = "123456";

/**
 * O segundo passo do login da equipe (ameaça T8): depois da senha, a tela da verificação em duas etapas pede o
 * código do app autenticador.
 */
export async function passTwoFactor(page: Page, code = ADMIN_TOTP) {
  await expect(page).toHaveURL(/\/entrar\/verificacao$/);
  await page.getByLabel("Código do app autenticador").fill(code);
  await page.getByRole("button", { name: "Confirmar" }).click();
}
