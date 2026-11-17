import { expect, test } from "@playwright/test";

const ACCOUNT = "0199f6a0-0000-7000-8000-0000000005e7";

test("esqueci minha senha: pede o link, cria a senha nova e entra com ela", async ({ page }) => {
  await page.goto("/entrar");
  await page.getByRole("link", { name: "Esqueci minha senha" }).click();
  await expect(page).toHaveURL(/\/conta\/esqueci-a-senha$/);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Esqueci minha senha");

  await page.getByLabel("E-mail").fill("redefinir@exemplo.com");
  await page.getByRole("button", { name: "Mandar o link" }).click();
  // a mesma resposta exista a conta ou não
  await expect(page.getByRole("status")).toContainText("Se houver uma conta com redefinir@exemplo.com");
  await expect(page.getByRole("heading", { name: "Confira seu e-mail" })).toBeFocused();

  // o link do e-mail; um token por execução, porque ele vale uma vez só
  const token = `token-bom-${test.info().testId}-${test.info().retry}`;
  await page.goto(`/conta/redefinir-senha?conta=${ACCOUNT}&token=${token}`);
  await page.getByLabel("Senha nova", { exact: true }).fill("curta");
  await page.getByRole("button", { name: "Trocar a senha" }).click();
  await expect(page.getByText("A senha precisa de pelo menos 8 caracteres.")).toBeVisible();
  await expect(page.getByLabel("Senha nova", { exact: true })).toBeFocused();

  await page.getByLabel("Senha nova", { exact: true }).fill("minha-senha-nova");
  await page.getByLabel("Repita a senha nova").fill("minha-senha-nova");
  await page.getByRole("button", { name: "Trocar a senha" }).click();
  await expect(page.getByText("Senha trocada. Agora é só entrar com a senha nova.")).toBeVisible();

  await page.getByRole("status").getByRole("link", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/entrar$/);
  await page.getByLabel("E-mail").fill("redefinir@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("minha-senha-nova");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).not.toHaveURL(/\/entrar/);

  // o mesmo link outra vez: já foi usado
  await page.goto(`/conta/redefinir-senha?conta=${ACCOUNT}&token=${token}`);
  await page.getByLabel("Senha nova", { exact: true }).fill("outra-senha-nova");
  await page.getByLabel("Repita a senha nova").fill("outra-senha-nova");
  await page.getByRole("button", { name: "Trocar a senha" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "Este link venceu ou já foi usado" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Pedir outro link" })).toBeFocused();
});

test("link sem token leva a pedir outro", async ({ page }) => {
  await page.goto("/conta/redefinir-senha");
  await expect(page.getByRole("alert").filter({ hasText: "Este link venceu ou já foi usado" })).toBeVisible();
  await page.getByRole("link", { name: "Pedir outro link" }).click();
  await expect(page).toHaveURL(/\/conta\/esqueci-a-senha$/);
});
