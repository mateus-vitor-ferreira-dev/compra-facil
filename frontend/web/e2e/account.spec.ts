import { expect, test, type Page } from "@playwright/test";

async function signIn(page: Page, email: string, password = "senha-boa-123", path = "/entrar") {
  await page.goto(path);
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Entrar" }).click();
}

test("a conta pede login e volta para ela depois", async ({ page }) => {
  await page.goto("/conta");
  await expect(page).toHaveURL(/\/entrar\?voltar=%2Fconta$/);
  await expect(page.getByRole("link", { name: "Entrar" }).first()).toBeVisible();

  await page.getByLabel("E-mail").fill("ana@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/conta$/);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Olá, Ana");
  await expect(page.getByRole("banner").getByRole("link", { name: "Minha conta" })).toBeVisible();
});

test("senha errada avisa e devolve o foco à senha", async ({ page }) => {
  await signIn(page, "ana@exemplo.com", "errada-123");
  await expect(page.getByRole("alert").filter({ hasText: "E-mail ou senha incorretos." })).toBeVisible();
  await expect(page.getByLabel("Senha", { exact: true })).toBeFocused();
  await expect(page.getByLabel("E-mail")).toHaveValue("ana@exemplo.com");
});

test("e-mail não confirmado oferece mandar o link de novo", async ({ page }) => {
  await signIn(page, "confirmar@exemplo.com");
  await expect(page.getByRole("alert").filter({ hasText: "Confirme seu e-mail" })).toBeVisible();
  await page.getByRole("button", { name: "Mandar o link de novo" }).click();
  await expect(page.getByText("o link chega em instantes")).toBeVisible();
});

test("?voltar= só leva a páginas da loja", async ({ page }) => {
  await signIn(page, "ana@exemplo.com", "senha-boa-123", "/entrar?voltar=//evil.example");
  await expect(page).toHaveURL("http://localhost:3000/conta");

  await page.getByRole("button", { name: "Sair" }).click();
  await expect(page).toHaveURL("http://localhost:3000/");
  await signIn(page, "ana@exemplo.com", "senha-boa-123", "/entrar?voltar=%2Fcarrinho");
  await expect(page).toHaveURL(/\/carrinho$/);
});

test("cadastro valida em português, foca o resumo dos erros e entra direto", async ({ page }) => {
  await page.goto("/cadastro");
  await page.getByRole("button", { name: "Criar conta" }).click();
  // cada erro fica junto do campo (aria-describedby), além do resumo
  await expect(page.getByLabel("Nome")).toHaveAccessibleDescription("Escreva seu nome.");
  await expect(page.getByLabel("E-mail")).toHaveAccessibleDescription("Escreva seu e-mail.");
  await expect(page.getByLabel("Senha", { exact: true })).toHaveAccessibleDescription(
    /A senha precisa de pelo menos 8 caracteres\./,
  );
  const summary = page.getByRole("alert").filter({ hasText: "3 campos precisam de ajuste" });
  await expect(summary).toBeFocused();
  await summary.getByRole("link", { name: /^Senha:/ }).click();
  await expect(page).toHaveURL(/#cadastro-password$/);
  await expect(page.getByLabel("Nome")).toHaveAttribute("aria-invalid", "true");

  await page.getByLabel("Nome").fill("Bruno Lima");
  await page.getByLabel("E-mail").fill("ana@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("uma frase boa");
  await page.getByRole("button", { name: "Criar conta" }).click();
  await expect(page.getByLabel("E-mail")).toHaveAccessibleDescription(
    /Já existe uma conta com este e-mail\./,
  );

  await page.getByLabel("E-mail").fill(`bruno.${Date.now()}@exemplo.com`);
  await page.getByLabel("Senha", { exact: true }).fill("uma frase boa");
  await page.getByRole("button", { name: "Criar conta" }).click();
  await expect(page).toHaveURL(/\/conta$/);
  await expect(page.getByRole("heading", { name: "Nenhum pedido ainda" })).toBeVisible();
});

test("meus pedidos: situação, motivo do cancelamento e o detalhe", async ({ page }) => {
  await signIn(page, "ana@exemplo.com");
  await expect(page).toHaveURL(/\/conta$/);
  const orders = page.getByRole("region", { name: "Meus pedidos" });
  await expect(orders.getByRole("link")).toHaveCount(3);
  await expect(orders).toContainText("Pedido 4F2A9C1B");
  await expect(orders).toContainText("Em preparação");
  await expect(orders).toContainText("Cancelado");

  // o pedido em preparação mostra a nota fiscal, com a chave em grupos de 4 como no DANFE
  await orders.getByRole("link", { name: /Pedido 4F2A9C1B/ }).click();
  const invoice = page.getByRole("region", { name: "Nota fiscal" });
  await expect(invoice).toContainText("NF-e 1001, série 1");
  await expect(invoice).toContainText("3126 1011 2223 3300 0181 5500 1000 0010 0110 0001 0015");
  const [download] = await Promise.all([
    page.waitForEvent("download"),
    invoice.getByRole("link", { name: "Baixar a nota (PDF)" }).click(),
  ]);
  expect(download.suggestedFilename()).toBe("nfe-1001.pdf");
  await page.goBack();

  await orders.getByRole("link", { name: /Pedido 7C3E5D2A/ }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Pedido 7C3E5D2A");
  await expect(page.getByText("Um dos produtos esgotou antes da reserva. Nada foi cobrado.")).toBeVisible();
  await expect(page.getByText("2 × R$ 2.639,92")).toBeVisible();

  expect((await page.goto("/conta/pedidos/0199f6a0-0000-7000-8000-0000000fffff"))?.status()).toBe(404);
});

test("o pedido entregue aceita a desistência em 7 dias, com confirmação", async ({ page }) => {
  await signIn(page, "ana@exemplo.com");
  await expect(page).toHaveURL(/\/conta$/);
  await page.goto("/conta/pedidos/9d1b6e3f-0000-7000-8000-000000000003");
  const section = page.getByRole("region", { name: "Desistir da compra" });
  await expect(section).toContainText("7 dias depois da entrega");

  // a primeira tecla só abre a pergunta, com o valor e a forma do estorno; manter devolve o foco
  await section.getByRole("button", { name: "Desistir da compra" }).click();
  await expect(section.getByRole("heading", { name: "R$ 2.639,92 de volta" })).toBeFocused();
  await expect(section).toContainText("no cartão usado na compra");
  await section.getByRole("button", { name: "Manter o pedido" }).click();
  await expect(section.getByRole("button", { name: "Desistir da compra" })).toBeFocused();

  await section.getByRole("button", { name: "Desistir da compra" }).click();
  await section.getByRole("button", { name: "Confirmar a desistência" }).click();
  await expect(page).toHaveURL(/desistencia=1/);
  await expect(page.getByRole("status").filter({ hasText: "Desistência registrada" })).toBeVisible();
  await expect(page.getByText("Valor estornado", { exact: true }).first()).toBeVisible();
  await expect(page.getByRole("region", { name: "Desistir da compra" })).toHaveCount(0);
});

test("sair apaga a sessão e o cabeçalho volta a oferecer Entrar", async ({ page, context }) => {
  await signIn(page, "ana@exemplo.com");
  await page.getByRole("button", { name: "Sair" }).click();
  await expect(page).toHaveURL("http://localhost:3000/");
  await expect(page.getByRole("banner").getByRole("link", { name: "Entrar" })).toBeVisible();
  expect(
    (await context.cookies()).filter((c) => c.name.startsWith("cf_") && c.name !== "cf_cart"),
  ).toHaveLength(0);
});

test("o link do e-mail confirma a conta com um toque", async ({ page }) => {
  const account = "0199f6a0-0000-7000-8000-000000000a1a";
  await page.goto(`/conta/confirmar-email?conta=${account}&token=token-velho`);
  await page.getByRole("button", { name: "Confirmar meu e-mail" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "Este link venceu" })).toBeVisible();

  await page.goto(`/conta/confirmar-email?conta=${account}&token=token-bom`);
  await page.getByRole("button", { name: "Confirmar meu e-mail" }).click();
  await expect(page.getByText("E-mail confirmado. Agora é só entrar.")).toBeVisible();
});
