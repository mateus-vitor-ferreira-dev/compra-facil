import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";

async function audit(page: Page) {
  const results = await new AxeBuilder({ page })
    .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
    .analyze();
  expect(results.violations.map((v) => v.id)).toEqual([]);
}

/**
 * O aviso de manutenção (#370). O estado de teste fica num espaço próprio (cookie), para a manutenção daqui não
 * segurar o checkout dos outros testes, que rodam em paralelo no mesmo servidor.
 */
const TV = "/produto/0199f6a0-0000-7000-8000-000000000000";

async function ownSpace(page: Page) {
  await page
    .context()
    .addCookies([{ name: "cf_teste_manutencao", value: crypto.randomUUID(), url: "http://localhost:3000" }]);
}

async function deploying(page: Page, on: boolean) {
  const response = await page.request.post("/api/teste/manutencao", { data: { deploying: on } });
  expect(response.status()).toBe(204);
}

test("durante o deploy, a loja avisa que está sendo atualizada no lugar do erro, e volta sozinha", async ({
  page,
}) => {
  await ownSpace(page);
  // sem deploy, a API fora é erro
  await page.goto("/busca?q=fora");
  await expect(page.getByRole("heading", { name: "Os produtos não carregaram" })).toBeVisible();

  await deploying(page, true);
  await page.goto("/busca?q=fora");
  await expect(
    page.getByRole("status").filter({ hasText: "Estamos atualizando a loja. Leva cerca de 1 minuto" }),
  ).toBeVisible();
  await expect(page.getByRole("heading", { name: "Estamos atualizando a loja" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Tentar agora" })).toBeVisible();
  await audit(page);

  // o deploy acabou: a página confere sozinha e a faixa some
  await deploying(page, false);
  await expect(page.getByRole("heading", { name: "Os produtos não carregaram" })).toBeVisible({
    timeout: 15_000,
  });
  await expect(page.getByText("Estamos atualizando a loja. Leva cerca")).toHaveCount(0);
});

test("durante o deploy, o pedido espera com a explicação e o que foi preenchido fica", async ({ page }) => {
  await ownSpace(page);
  await page.goto(TV);
  await page.locator("form[action] input[name=quantity]").waitFor();
  await page.getByRole("button", { name: "Adicionar ao carrinho" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Adicionado ao carrinho" })).toBeVisible();

  await deploying(page, true);
  await page.goto("/checkout");
  await page.getByLabel("E-mail").fill("manutencao@exemplo.com");
  const button = page.getByRole("button", { name: "Aguardando a atualização…" });
  await expect(button).toHaveAttribute("aria-disabled", "true");
  await expect(
    page.getByText("Os pedidos voltam em cerca de 1 minuto. O que você preencheu fica aqui"),
  ).toBeVisible();
  await button.click({ force: true }); // o clique de quem insiste: aria-disabled, o Playwright não clicaria
  await expect(page).toHaveURL(/\/checkout$/); // nada foi enviado
  await audit(page);

  await deploying(page, false);
  await expect(page.getByRole("button", { name: /^Fazer o pedido de/ })).toBeVisible({ timeout: 15_000 });
  // a volta é anunciada na mesma região de status da pausa
  await expect(
    page.getByRole("status").filter({ hasText: "A loja foi atualizada. Confira e faça o pedido." }),
  ).toBeVisible();
  await expect(page.getByLabel("E-mail")).toHaveValue("manutencao@exemplo.com");
  await deploying(page, false);
});

test("sem JavaScript, o pedido feito durante o deploy volta com o aviso e os dados", async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await ownSpace(page);
  await page.goto(TV);
  await page.getByRole("button", { name: "Adicionar ao carrinho" }).click();
  await deploying(page, true);
  await page.goto("/checkout");
  await page.getByLabel("E-mail").fill("sem-js@exemplo.com");
  // sem JavaScript, o botão envia o formulário mesmo assim: quem segura é o servidor
  await page.getByRole("button", { name: "Aguardando a atualização…" }).click({ force: true });
  await expect(page.getByRole("alert")).toContainText("Estamos atualizando a loja");
  await expect(page.getByRole("alert")).toContainText("Nada foi cobrado");
  await expect(page.getByLabel("E-mail")).toHaveValue("sem-js@exemplo.com");
  await context.close();
});

test("a manutenção agendada pelo painel aparece na loja, e cancelada some", async ({ page }) => {
  await ownSpace(page);
  await page.goto("/admin/manutencao");
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Manutenção" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Manutenção" })).toHaveAttribute("aria-current", "page");

  // daqui a dois dias, às 22:00 de Brasília (a janela tem até 90 dias de antecedência)
  const day = new Intl.DateTimeFormat("en-CA", { timeZone: "America/Sao_Paulo" }).format(
    Date.now() + 2 * 86_400_000,
  );
  const label = new Intl.DateTimeFormat("pt-BR", { day: "numeric", month: "short", timeZone: "UTC" }).format(
    new Date(`${day}T12:00:00Z`),
  );

  // o fim antes do início é recusado no campo, e a janela longe demais também
  await page.getByLabel("Começa em").fill(`${day}T22:00`);
  await page.getByLabel("Termina em").fill(`${day}T21:00`);
  await page.getByRole("button", { name: "Agendar a manutenção" }).click();
  await expect(page.getByLabel("Termina em")).toHaveAccessibleDescription(/O fim vem depois do início/);
  await page.getByLabel("Começa em").fill("2099-11-20T22:00");
  await page.getByLabel("Termina em").fill("2099-11-20T22:05");
  await page.getByRole("button", { name: "Agendar a manutenção" }).click();
  await expect(page.getByLabel("Começa em")).toHaveAccessibleDescription(/até 90 dias de antecedência/);

  await page.getByLabel("Começa em").fill(`${day}T22:00`);
  await page.getByLabel("Termina em").fill(`${day}T22:05`);
  await page
    .getByLabel("Aviso para quem compra (opcional)")
    .fill("Os pedidos feitos antes continuam normalmente.");
  await page.getByRole("button", { name: "Agendar a manutenção" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Manutenção agendada." })).toBeVisible();
  await expect(page.getByText(`Agendada: ${label}, das 22:00 às 22:05`)).toBeVisible();

  await page.goto("/");
  const banner = page.getByRole("status").filter({ hasText: "Manutenção programada" });
  await expect(banner).toContainText(`Manutenção programada: ${label}, das 22:00 às 22:05.`);
  await expect(banner).toContainText("Os pedidos feitos antes continuam normalmente.");

  await page.goto("/admin/manutencao");
  await page.getByRole("button", { name: "Cancelar a manutenção" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Manutenção cancelada." })).toBeVisible();
  await page.goto("/");
  await expect(page.getByText("Manutenção programada")).toHaveCount(0);
});
