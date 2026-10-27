import { expect, test } from "@playwright/test";

test("conta nova pelo Google entra direto e volta para onde estava", async ({ page }) => {
  await page.goto("/entrar?voltar=%2Fcarrinho");
  await page.getByRole("link", { name: "Continuar com o Google" }).click();
  await expect(page.getByRole("heading", { name: "Escolha uma conta" })).toBeVisible();
  await page.getByRole("link", { name: /^nova-.*@gmail\.com$/ }).click();
  await expect(page).toHaveURL(/\/carrinho$/);
  await page.goto("/conta");
  await expect(page.getByTestId("account-email")).toContainText("@gmail.com");
});

test("e-mail que já tem conta pede a senha uma vez para vincular", async ({ page, request }) => {
  // uma conta com senha só deste teste (a mesma pessoa tem um Google com esse e-mail)
  const email = `vincular-${Date.now()}-${Math.random().toString(36).slice(2, 6)}@exemplo.com`;
  await request.post("http://localhost:4010/identity/v1/accounts", {
    data: { email, password: "senha-boa-123", name: "Bia" },
  });
  await page.goto("/entrar?voltar=%2Fconta");
  await page.getByRole("link", { name: "Continuar com o Google" }).click();
  await page.getByRole("link", { name: email }).click();

  await expect(page).toHaveURL(/\/entrar\/vincular$/);
  await expect(page.getByText(`Já existe uma conta com ${email}.`, { exact: false })).toBeVisible();
  await page.getByLabel("Senha da conta").fill("senha-errada-1");
  await page.getByRole("button", { name: "Vincular e entrar" }).click();
  await expect(
    page.getByText("Senha incorreta. É a senha desta conta na loja, não a do Google."),
  ).toBeVisible();

  await page.getByLabel("Senha da conta").fill("senha-boa-123");
  await page.getByRole("button", { name: "Vincular e entrar" }).click();
  await expect(page).toHaveURL(/\/conta$/);
  await expect(page.getByTestId("account-email")).toContainText(email);

  // daí em diante, o Google sozinho entra
  await page.getByRole("button", { name: "Sair" }).click();
  await page.goto("/entrar?voltar=%2Fconta");
  await page.getByRole("link", { name: "Continuar com o Google" }).click();
  await page.getByRole("link", { name: email }).click();
  await expect(page).toHaveURL(/\/conta$/);
});

test("cancelar no Google volta ao login explicando", async ({ page }) => {
  await page.goto("/entrar");
  await page.getByRole("link", { name: "Continuar com o Google" }).click();
  await page.getByRole("link", { name: "Cancelar" }).click();
  await expect(page).toHaveURL(/\/entrar\?erro=google-cancelado$/);
  await expect(
    page.getByRole("alert").filter({ hasText: "O login com o Google foi cancelado" }),
  ).toBeVisible();
});

test("callback sem o login em andamento (state de outro navegador) é recusado", async ({ page }) => {
  await page.goto("/api/auth/google/callback?code=qualquer&state=forjado");
  await expect(page).toHaveURL(/\/entrar\?erro=google$/);
  await expect(page.getByRole("alert").filter({ hasText: "O Google não confirmou o login" })).toBeVisible();
});

test("o cadastro também oferece o Google", async ({ page }) => {
  await page.goto("/cadastro");
  await expect(page.getByRole("link", { name: "Continuar com o Google" })).toHaveAttribute(
    "href",
    "/api/auth/google/start?voltar=%2Fconta",
  );
});
