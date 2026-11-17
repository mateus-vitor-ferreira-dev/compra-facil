import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { ADMIN_TOTP, passTwoFactor } from "./two-factor";

/**
 * A verificação em duas etapas da equipe (ameaça T8): a senha sozinha não abre o painel. Quem já tem o app
 * autenticador digita o código (ou um de recuperação); no primeiro login, cadastra o app pelo QR code.
 */
async function password(page: Page, email: string, path = "/admin") {
  await page.goto(path);
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
}

// o título chega depois do redirect da server action: espera por ele antes de checar a página
async function axe(page: Page, title: RegExp) {
  await expect(page).toHaveTitle(title);
  return new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21aa", "wcag22aa"]);
}

test("a senha leva ao código; o código errado fica na tela e o certo abre o painel", async ({ page }) => {
  await password(page, "admin@exemplo.com", "/admin/pedidos");

  await expect(page).toHaveURL(/\/entrar\/verificacao$/);
  await expect(page.getByRole("heading", { level: 1, name: "Verificação em duas etapas" })).toBeVisible();
  // a sessão ainda não existe: o painel manda de volta para o login
  const field = page.getByLabel("Código do app autenticador");
  await expect(field).toHaveAttribute("autocomplete", "one-time-code");
  await expect(field).toHaveAttribute("inputmode", "numeric");
  expect((await (await axe(page, /Verificação em duas etapas/)).analyze()).violations).toEqual([]);

  await field.fill("000000");
  await page.getByRole("button", { name: "Confirmar" }).click();
  await expect(page.getByText(/Código incorreto/)).toBeVisible();
  await expect(field).toBeFocused();

  await field.fill(ADMIN_TOTP.slice(0, 3) + " " + ADMIN_TOTP.slice(3));
  await page.getByRole("button", { name: "Confirmar" }).click();
  await expect(page).toHaveURL(/\/admin\/pedidos$/);
});

test("sem o celular, um código de recuperação entra uma vez", async ({ page, context }) => {
  await password(page, "admin@exemplo.com");
  await page.getByRole("link", { name: "Usar um código de recuperação" }).click();
  await page.getByLabel("Código de recuperação").fill("RCOD7-K7M2P");
  await page.getByRole("button", { name: "Confirmar" }).click();
  await expect(page).toHaveURL(/\/admin$/);

  // o mesmo código não vale de novo
  await context.clearCookies();
  await password(page, "admin@exemplo.com");
  await page.getByRole("link", { name: "Usar um código de recuperação" }).click();
  await page.getByLabel("Código de recuperação").fill("rcod7-k7m2p");
  await page.getByRole("button", { name: "Confirmar" }).click();
  await expect(page.getByText(/incorreto ou já usado/)).toBeVisible();
});

test("a tela do código sem o tíquete volta para a senha", async ({ page }) => {
  await page.goto("/entrar/verificacao");

  await expect(page).toHaveURL(/\/entrar\?erro=verificacao-expirada$/);
  await expect(page.getByText(/A verificação em duas etapas venceu/)).toBeVisible();
});

test("primeiro login da equipe: cadastra o app pelo QR code e guarda os códigos de recuperação", async ({
  page,
}) => {
  await password(page, "admin@novo.exemplo.com");

  await expect(page).toHaveURL(/\/entrar\/autenticador$/);
  await expect(
    page.getByRole("heading", { level: 1, name: "Ative a verificação em duas etapas" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Gerar o QR code" }).click();
  const qr = page.getByRole("img", { name: /QR code para cadastrar a Embrulhado/ });
  await expect(qr).toBeVisible();
  await expect(qr.locator("svg")).toHaveCount(1); // gerado no servidor, sem imagem de fora
  await expect(page.getByText("jbsw y3dp ehpk 3pxp")).toBeVisible();
  expect((await (await axe(page, /Ativar a verificação em duas etapas/)).analyze()).violations).toEqual([]);

  await page.getByLabel("Código do app autenticador").fill(ADMIN_TOTP);
  await page.getByRole("button", { name: "Ativar e entrar" }).click();

  await expect(page).toHaveURL(/\/entrar\/codigos-de-recuperacao$/);
  const codes = page.getByRole("region", { name: "Seus códigos de recuperação" });
  await expect(codes.getByRole("listitem")).toHaveCount(10);
  await expect(page.getByText(/não aparecem de novo/)).toBeVisible();
  expect((await (await axe(page, /Códigos de recuperação/)).analyze()).violations).toEqual([]);
  await page.getByRole("button", { name: "Guardei os códigos, continuar" }).click();
  await expect(page).toHaveURL(/\/admin$/);

  // os códigos aparecem uma vez só
  await page.goto("/entrar/codigos-de-recuperacao");
  await expect(page).toHaveURL(/\/admin$/);
});

test("Segurança da conta: a 2FA ativa e códigos de recuperação novos com o código do app", async ({
  page,
}) => {
  await password(page, "admin@exemplo.com", "/admin/seguranca");
  await passTwoFactor(page);
  await expect(page).toHaveURL(/\/admin\/seguranca$/);

  await expect(page.getByText("Autenticação em duas etapas", { exact: true })).toBeVisible();
  await expect(page.getByText("ativa", { exact: true })).toBeVisible();
  await page.getByLabel("Código do app autenticador").fill("111111");
  await page.getByRole("button", { name: "Gerar códigos novos" }).click();
  await expect(page.getByText(/Código incorreto/)).toBeVisible();

  await page.getByLabel("Código do app autenticador").fill(ADMIN_TOTP);
  await page.getByRole("button", { name: "Gerar códigos novos" }).click();
  const codes = page.getByRole("region", { name: "Seus códigos de recuperação" });
  await expect(codes.getByRole("listitem").filter({ hasText: /^novo\d-k7m2p$/ })).toHaveCount(10);
  expect((await (await axe(page, /Segurança da conta/)).analyze()).violations).toEqual([]);
});
